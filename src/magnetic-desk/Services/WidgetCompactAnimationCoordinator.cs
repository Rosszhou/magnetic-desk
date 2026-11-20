// Copyright (c) DeskBox. All rights reserved.

using System.Diagnostics;
using MagneticDesk.Helpers;
using MagneticDesk.Models;
using MagneticDesk.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MagneticDesk.Services;

/// <summary>
/// Shares an active-only frame clock across dragging, resizing, capsule and tray
/// animations. Display timing is a budget; callbacks are not proof of presentation.
/// </summary>
internal static class WidgetCompactAnimationCoordinator
{
    // A native bounds/clip transition still has one UI-thread coordinator, but
    // multiple capsules may animate concurrently (e.g. one collapsing while the
    // cursor expands the next). Allowing several in-flight transitions avoids
    // dropping a capsule's animation when the slot is occupied. First-frame
    // commit pressure is absorbed by the expansion warm-up instead of by
    // serializing transitions.
    internal const int MaximumConcurrentBoundsTransitions = int.MaxValue;

    /// <summary>
    /// How long a hover-pre-armed idle clock stays up without a real
    /// registration. Hover intent precedes every capsule expansion, so a
    /// short window keeps repeated hover/expand cycles warm without holding
    /// the compositor clock boost at rest.
    /// </summary>
    internal const double PreArmLingerSeconds = 5;

    private static readonly Dictionary<long, Action> FrameCallbacks = [];
    private static readonly Dictionary<long, Action<long>> PreparedTransitions = [];
    private static readonly Dictionary<long, FrameTarget> FrameTargets = [];
    private static KeyValuePair<long, Action>[] s_frameCallbackSnapshot = [];
    private static bool s_frameCallbackSnapshotDirty;
    private static readonly HashSet<long> BoundsTransitionRegistrations = [];
    private static readonly Dictionary<IntPtr, PendingBoundsMove> PendingBoundsMoves = [];
    private static long s_nextRegistrationId;
    private static bool s_isRenderingSubscribed;
    private static bool s_isDispatchingFrame;
    private static int s_boundsPreparationDepth;
    private static IDisposable? s_clockBoostLease;
    private static bool s_isClockPreArmed;
    private static DispatcherQueue? s_preArmLingerDispatcher;
    private static DispatcherQueueTimer? s_preArmLingerTimer;
    private static DispatcherQueue? s_frameDispatcher;
    private static DispatcherQueueTimer? s_refreshFrameTimer;
    private static bool s_refreshFramePending;
    private static DwmClockRun? s_dwmClockRun;

    // Session-level tick health, sampled on the UI thread from every frame
    // dispatch. The recent overrun bitmask feeds the interaction-time backdrop
    // simplification decision (see InteractionBackdropSimplificationPolicy).
    private static long s_lastFrameTickTimestamp;
    private static double s_frameTickBudgetMs;
    private static long s_recentOverrunMask;

    // Diagnostics for that same mask. The interaction-backdrop policy reads it,
    // but its only log site sits inside the Win10-only downgrade path, so on
    // Win11 frame overruns are unobservable. Throttled to one line per second
    // and silent while frames keep their budget.
    private static long s_lastOverrunLogTimestamp;
    private static int s_overrunsSinceLog;
    private static int s_framesSinceLog;

    private enum FrameClockSource
    {
        None,
        CompositionRendering,
        DwmFlushThread,
        RefreshTimer
    }

    private static FrameClockSource s_clockSource =
        FrameClockSource.None;

    private sealed class DwmClockRun(DispatcherQueue dispatcher)
    {
        internal readonly DispatcherQueue Dispatcher = dispatcher;
        internal volatile bool IsActive = true;
        internal int PendingTick;
        internal int InstantFlushCount;
        internal double TargetIntervalMilliseconds;
        internal readonly WidgetFrameClockHealthPolicy Health = new();
    }

    private sealed class FrameTarget(IntPtr windowHandle, Func<IEnumerable<IntPtr>>? windows, bool paceToDisplay,
        Func<double>? frameBudget = null)
    {
        internal readonly IntPtr WindowHandle = windowHandle;
        internal readonly Func<IEnumerable<IntPtr>>? Windows = windows;
        internal readonly bool PaceToDisplay = paceToDisplay;
        internal readonly Func<double>? FrameBudget = frameBudget;
        internal readonly WidgetAnimationFramePacingPolicy Pacing = new();
        internal bool Initialized;
        internal double BudgetMilliseconds = 1000d / 60;
    }

    private readonly record struct PendingBoundsMove(
        IntPtr WindowHandle,
        RectInt32 Bounds,
        uint Flags,
        Action BeforeCommit,
        Action<bool, double> AfterCommit,
        Action Fallback);

    public static IDisposable Register(Action frameCallback, IntPtr windowHandle = default, bool paceToDisplay = false)
    {
        return RegisterCore(frameCallback, false, new FrameTarget(windowHandle, null, paceToDisplay));
    }

    public static IDisposable Register(Action frameCallback, Func<IEnumerable<IntPtr>> windowHandles)
    {
        ArgumentNullException.ThrowIfNull(windowHandles);
        return RegisterCore(frameCallback, false, new FrameTarget(IntPtr.Zero, windowHandles, false));
    }

    public static double GetFrameBudgetMilliseconds(IntPtr windowHandle) =>
        WidgetAnimationDisplayTiming.GetFrameBudgetMilliseconds(windowHandle);

    public static double GetFrameBudgetMillisecondsForPoint(int screenX, int screenY) =>
        WidgetAnimationDisplayTiming.GetFrameBudgetMillisecondsForPoint(screenX, screenY);

    public static IDisposable Register(Action frameCallback, Func<double> frameBudgetMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(frameBudgetMilliseconds);
        return RegisterCore(frameCallback, false, new FrameTarget(IntPtr.Zero, null, false, frameBudgetMilliseconds));
    }

    public static bool HasBoundsTransitionCapacity =>
        WidgetCompactAnimationConcurrencyPolicy.ShouldAnimate(
            BoundsTransitionRegistrations.Count,
            MaximumConcurrentBoundsTransitions);

    internal static bool HasActiveAnimations => FrameCallbacks.Count > 0;

    internal static long RecentFrameOverrunMask => s_recentOverrunMask;

    public static IDisposable RegisterBoundsTransition(Action frameCallback, IntPtr windowHandle = default)
    {
        if (!HasBoundsTransitionCapacity)
        {
            throw new InvalidOperationException("No compact bounds-transition animation slot is available.");
        }

        return RegisterCore(frameCallback, true, new FrameTarget(windowHandle, null, false));
    }

    internal static IDisposable StartOnPreparedFrame(Action<long> start)
    {
        long id = ++s_nextRegistrationId;
        PreparedTransitions.Add(id, start);
        if (PreparedTransitions.Count == 1) CompositionTarget.Rendering += OnPreparedFrame;
        return new PreparedTransitionRegistration(id);
    }

    internal static IDisposable PrepareBoundsBatch()
    {
        s_boundsPreparationDepth++;
        return new BoundsPreparationScope();
    }

    private sealed class BoundsPreparationScope : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--s_boundsPreparationDepth == 0) FlushPendingBoundsMoves();
        }
    }

    private static void OnPreparedFrame(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnPreparedFrame;
        var pending = PreparedTransitions.ToArray();
        PreparedTransitions.Clear();
        long timestamp = Stopwatch.GetTimestamp();
        foreach (var entry in pending)
        {
            try { entry.Value(timestamp); }
            catch (Exception ex) { App.Log($"[CompactComposition] Prepared transition failed: {ex}"); }
        }
    }

    private sealed class PreparedTransitionRegistration(long id) : IDisposable
    {
        public void Dispose()
        {
            PreparedTransitions.Remove(id);
            if (PreparedTransitions.Count == 0) CompositionTarget.Rendering -= OnPreparedFrame;
        }
    }

    /// <summary>
    /// Starts the shared frame clock before the first real registration. The
    /// first capsule expansion of a session otherwise pays clock startup —
    /// boost lease, Win10 DwmFlush thread, display-timing cache — inside its
    /// opening frames. Pointer-entered hover intent is the caller. The linger
    /// timer tears an unused clock back down.
    /// </summary>
    internal static void PreArmFrameClock(IntPtr windowHandle)
    {
        if (s_isRenderingSubscribed)
        {
            return;
        }

        DispatcherQueue? dispatcher = DispatcherQueue.GetForCurrentThread();
        if (windowHandle != IntPtr.Zero)
        {
            // Populate this monitor's budget and start the background topology
            // query away from the animation's first frame.
            _ = GetFrameBudgetMilliseconds(windowHandle);
        }

        if (dispatcher is null)
        {
            // Without a dispatcher there is no Win10 pacer and no linger
            // teardown owner; leave the clock cold instead of subscribing an
            // unowned frame source.
            return;
        }

        s_isClockPreArmed = true;
        s_clockBoostLease ??= CompositorClockBoostCoordinator.Acquire();
        s_isRenderingSubscribed = true;
        StartFrameClock();
        if (s_preArmLingerTimer is null ||
            !ReferenceEquals(s_preArmLingerDispatcher, dispatcher))
        {
            s_preArmLingerDispatcher = dispatcher;
            s_preArmLingerTimer = dispatcher.CreateTimer();
            s_preArmLingerTimer.IsRepeating = false;
            s_preArmLingerTimer.Tick += OnPreArmLingerTick;
        }

        s_preArmLingerTimer.Interval = TimeSpan.FromSeconds(PreArmLingerSeconds);
        s_preArmLingerTimer.Start();
    }

    private static void OnPreArmLingerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!s_isClockPreArmed || FrameCallbacks.Count > 0 || !s_isRenderingSubscribed)
        {
            return;
        }

        s_isClockPreArmed = false;
        s_isRenderingSubscribed = false;
        StopFrameClock();
        s_clockBoostLease?.Dispose();
        s_clockBoostLease = null;
        WidgetAnimationDisplayTiming.Clear();
        s_lastFrameTickTimestamp = 0;
    }

    /// <summary>
    /// Queues one real HWND bounds update for the current compositor tick. All
    /// concurrent capsule transitions are committed atomically after their
    /// callbacks finish, avoiding N independent DWM commits without changing
    /// the physical-window animation semantics.
    /// </summary>
    public static bool TryQueueBoundsMove(
        IntPtr windowHandle,
        RectInt32 bounds,
        uint flags,
        Action beforeCommit,
        Action<bool, double> afterCommit,
        Action fallback,
        bool deferUntilPrepared = false)
    {
        if ((!s_isDispatchingFrame && !(deferUntilPrepared && s_boundsPreparationDepth > 0)) || windowHandle == IntPtr.Zero)
        {
            return false;
        }

        PendingBoundsMoves[windowHandle] = new PendingBoundsMove(
            windowHandle,
            bounds,
            flags,
            beforeCommit,
            afterCommit,
            fallback);
        return true;
    }

    private static IDisposable RegisterCore(Action frameCallback, bool isBoundsTransition, FrameTarget target)
    {
        ArgumentNullException.ThrowIfNull(frameCallback);

        long registrationId = ++s_nextRegistrationId;
        FrameCallbacks.Add(registrationId, frameCallback);
        if (FrameCallbacks.Count == 1)
        {
            // Short transitions can fall entirely between the memory sampler's
            // ticks. Start a fresh quiet period explicitly for real visual work.
            App.NotifyMemoryCleanupActivity();
        }
        FrameTargets.Add(registrationId, target);
        s_frameCallbackSnapshotDirty = true;
        if (isBoundsTransition)
        {
            BoundsTransitionRegistrations.Add(registrationId);
        }
        if (!s_isRenderingSubscribed)
        {
            s_isRenderingSubscribed = true;
            s_clockBoostLease = CompositorClockBoostCoordinator.Acquire();
            StartFrameClock();
        }
        else if (s_isClockPreArmed && FrameCallbacks.Count == 1)
        {
            // The first real registration adopts a running pre-armed clock.
            // Idle-linger ticks must not seed the overrun mask that the
            // interaction-backdrop simplification policy reads.
            s_recentOverrunMask = 0;
            s_lastFrameTickTimestamp = 0;
        }

        // Real registrations now own the clock; the last unregister tears it
        // down, so the pre-arm linger no longer applies.
        s_isClockPreArmed = false;
        s_preArmLingerTimer?.Stop();

        return new Registration(registrationId);
    }

    private static void StartFrameClock()
    {
        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
        {
            s_clockSource = FrameClockSource.CompositionRendering;
            CompositionTarget.Rendering += OnRendering;
            return;
        }

        s_frameDispatcher = dispatcherQueue;
        StartDwmFlushClock();
    }

    /// <summary>
    /// Uses the application's DWM flush completion as a pacing hint. Native
    /// window animations share this clock on Windows 10 and Windows 11, where
    /// XAML rendering callbacks can arrive below the active display rate.
    /// Each run owns its cancellation and queued tick: a stopped run cannot
    /// dispatch into a later interaction. This is not a per-output vblank API.
    /// </summary>
    private static void StartDwmFlushClock()
    {
        var run = new DwmClockRun(s_frameDispatcher!);
        s_dwmClockRun = run;
        s_clockSource = FrameClockSource.DwmFlushThread;
        RefreshFrameBudgets();
        s_lastFrameTickTimestamp = 0;
        var thread = new Thread(() => DwmFlushLoop(run))
        {
            IsBackground = true,
            Name = "MagneticDeskFramePacer",
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
        App.LogVerbose("[AnimationClock] shared source=DwmFlush");
    }

    /// <summary>
    /// Fallback clock tracks the fastest participating display, including mode
    /// changes. DwmFlush is retried on the next active run, not disabled forever.
    /// </summary>
    private static void StartRefreshTimer(DispatcherQueue dispatcherQueue)
    {
        RefreshFrameBudgets();
        TimeSpan interval = TimeSpan.FromMilliseconds(s_frameTickBudgetMs);
        s_clockSource = FrameClockSource.RefreshTimer;
        ResetFrameTickBudget(interval);
        s_refreshFrameTimer = dispatcherQueue.CreateTimer();
        s_refreshFrameTimer.Interval = interval;
        s_refreshFrameTimer.IsRepeating = true;
        s_refreshFrameTimer.Tick += OnRefreshTimerTick;
        s_refreshFrameTimer.Start();
        App.LogVerbose(
            $"[AnimationClock] shared source=DispatcherQueueTimer " +
            $"intervalMs={interval.TotalMilliseconds:F3} refreshHz={1000d / s_frameTickBudgetMs:F3}");
    }

    private static void DwmFlushLoop(DwmClockRun run)
    {
        while (run.IsActive)
        {
            long started = Stopwatch.GetTimestamp();
            if (!Win32Helper.TryDwmFlush())
            {
                SwitchToTimerFallback(run, "dwmapi-unavailable");
                return;
            }

            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (run.Health.RecordWait(elapsedMs, Volatile.Read(ref run.TargetIntervalMilliseconds)))
            {
                // Some mixed-output stacks pace this application to the slower
                // output. Do not let that clock cap a faster participating screen.
                SwitchToTimerFallback(run, "flush-slower-than-active-display");
                return;
            }
            if (elapsedMs < 0.5)
            {
                if (++run.InstantFlushCount >= 30)
                {
                    SwitchToTimerFallback(run, "dwmflush-not-pacing");
                    return;
                }
            }
            else
            {
                run.InstantFlushCount = 0;
            }

            if (run.IsActive && Interlocked.CompareExchange(ref run.PendingTick, 1, 0) == 0)
            {
                // XAML layout and paint must finish before another native resize.
                // Normal-priority ticks can keep the queue busy while the HWND
                // grows beyond the last rendered surface.
                if (!run.Dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => DispatchDwmFlushTick(run)))
                {
                    run.IsActive = false; // Dispatcher shutdown; never enqueue a replacement clock.
                    return;
                }
            }
        }
    }

    private static void DispatchDwmFlushTick(DwmClockRun run)
    {
        try
        {
            if (!run.IsActive || !ReferenceEquals(s_dwmClockRun, run)) return;
            OnRendering(sender: null, args: EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref run.PendingTick, 0);
        }
    }

    private static void SwitchToTimerFallback(DwmClockRun run, string reason)
    {
        run.IsActive = false;
        run.Dispatcher.TryEnqueue(() =>
        {
            if (!s_isRenderingSubscribed || !ReferenceEquals(s_dwmClockRun, run)) return;
            s_dwmClockRun = null;
            App.LogVerbose($"[AnimationClock] shared DwmFlush unavailable ({reason}); display timer for this run");
            StartRefreshTimer(run.Dispatcher);
        });
    }

    private static void OnRefreshTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!ReferenceEquals(sender, s_refreshFrameTimer) || !s_isRenderingSubscribed || s_refreshFramePending) return;
        s_refreshFramePending = true;
        if (!s_frameDispatcher!.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!ReferenceEquals(sender, s_refreshFrameTimer)) return;
                try { OnRendering(sender, args); }
                finally { s_refreshFramePending = false; }
            }))
        {
            s_refreshFramePending = false;
        }
    }

    private static void RefreshFrameBudgets()
    {
        double fastestBudget = double.PositiveInfinity;
        foreach (FrameTarget target in FrameTargets.Values)
        {
            double budget = double.PositiveInfinity;
            if (target.FrameBudget is not null)
            {
                double provided = target.FrameBudget();
                budget = double.IsFinite(provided) && provided > 0 ? provided : 1000d / 60;
            }
            else if (target.WindowHandle != IntPtr.Zero)
                budget = GetFrameBudgetMilliseconds(target.WindowHandle);
            else if (target.Windows is not null)
                foreach (IntPtr window in target.Windows())
                    if (window != IntPtr.Zero) budget = Math.Min(budget, GetFrameBudgetMilliseconds(window));
            target.BudgetMilliseconds = budget;
            fastestBudget = Math.Min(fastestBudget, budget);
        }
        if (!double.IsFinite(fastestBudget)) fastestBudget = GetFrameBudgetMilliseconds(IntPtr.Zero);
        if (s_dwmClockRun is { } run) Volatile.Write(ref run.TargetIntervalMilliseconds, fastestBudget);
        foreach (FrameTarget target in FrameTargets.Values)
            if (!double.IsFinite(target.BudgetMilliseconds)) target.BudgetMilliseconds = fastestBudget;
        if (Math.Abs(s_frameTickBudgetMs - fastestBudget) > 0.01)
        {
            ResetFrameTickBudget(TimeSpan.FromMilliseconds(fastestBudget));
            s_recentOverrunMask = 0;
        }
        if (s_refreshFrameTimer is not null &&
            Math.Abs(s_refreshFrameTimer.Interval.TotalMilliseconds - fastestBudget) > 0.01)
            s_refreshFrameTimer.Interval = TimeSpan.FromMilliseconds(fastestBudget);
    }

    /// <summary>
    /// Rolls one overrun bit per dispatched frame tick. All frame dispatches
    /// happen on the UI thread, so plain field access is safe.
    /// </summary>
    private static void RecordFrameTickCadence()
    {
        long now = Stopwatch.GetTimestamp();
        if (s_lastFrameTickTimestamp != 0 && s_frameTickBudgetMs > 0)
        {
            double intervalMs = Stopwatch
                .GetElapsedTime(s_lastFrameTickTimestamp, now)
                .TotalMilliseconds;
            if (intervalMs > 0)
            {
                bool overrun = intervalMs > s_frameTickBudgetMs *
                    WidgetCompactFrameSkipPolicy.OverrunBudgetFactor;
                s_recentOverrunMask = (s_recentOverrunMask << 1) | (overrun ? 1L : 0L);
                if (overrun)
                {
                    s_overrunsSinceLog++;
                }
            }
        }

        s_lastFrameTickTimestamp = now;
        s_framesSinceLog++;
        MaybeLogFrameOverruns(now);
    }

    /// <summary>
    /// Surfaces the frame-overrun mask, which is otherwise read only by the
    /// Win10 interaction-backdrop path. Verbose-gated, silent while frames keep
    /// their budget, at most one line per second.
    /// </summary>
    private static void MaybeLogFrameOverruns(long now)
    {
        if (!App.IsVerboseLoggingEnabled)
        {
            return;
        }

        bool firstTick = s_lastOverrunLogTimestamp == 0;
        if (!firstTick &&
            Stopwatch.GetElapsedTime(s_lastOverrunLogTimestamp, now).TotalSeconds < 1.0)
        {
            return;
        }

        if (firstTick)
        {
            App.LogVerbose(
                "[AnimationClock] frame-overrun monitor active: at most one line " +
                "per second, and only when frames miss the budget");
        }
        else if (s_overrunsSinceLog > 0)
        {
            App.LogVerbose(
                $"[AnimationClock] frame overruns={s_overrunsSinceLog}/{s_framesSinceLog} " +
                $"recent={System.Numerics.BitOperations.PopCount((ulong)s_recentOverrunMask)}/64 " +
                $"budgetMs={s_frameTickBudgetMs:F1}");
        }

        s_overrunsSinceLog = 0;
        s_framesSinceLog = 0;
        s_lastOverrunLogTimestamp = now;
    }

    private static void ResetFrameTickBudget(TimeSpan expectedInterval)
    {
        s_frameTickBudgetMs = Math.Max(1.0, expectedInterval.TotalMilliseconds);
        s_lastFrameTickTimestamp = 0;
    }

    private static void OnRendering(object? sender, object args)
    {
        if (!s_isRenderingSubscribed || s_isDispatchingFrame || s_boundsPreparationDepth > 0) return;
        // A pre-armed idle clock has no callbacks to dispatch, and its ticks
        // carry no evidence about real animation cadence.
        if (FrameCallbacks.Count == 0) return;
        RefreshFrameBudgets();
        RecordFrameTickCadence();
        PendingBoundsMoves.Clear();
        s_isDispatchingFrame = true;
        try
        {
            // Callbacks may complete and unregister themselves while this snapshot
            // is being dispatched. The registration check avoids invoking an entry
            // that another callback cancelled earlier in the same compositor tick.
            foreach ((long registrationId, Action callback) in GetFrameCallbackSnapshot())
            {
                if (!FrameCallbacks.ContainsKey(registrationId))
                {
                    continue;
                }

                try
                {
                    FrameTarget target = FrameTargets[registrationId];
                    double timestampMs = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
                    if (target.PaceToDisplay)
                    {
                        if (!target.Initialized)
                        {
                            target.Pacing.Reset(timestampMs, target.BudgetMilliseconds);
                            target.Initialized = true;
                        }
                        if (!target.Pacing.ShouldSubmit(timestampMs, target.BudgetMilliseconds)) continue;
                        // Direct manipulation follows the latest input, never an artificial
                        // load-dependent lag. Only the target display period limits its work.
                        target.Pacing.RecordSubmission(timestampMs, 0);
                    }
                    callback();
                }
                catch (Exception ex)
                {
                    App.Log($"[CompactAnimationClock] Frame callback failed: {ex.Message}");
                }
            }
        }
        finally
        {
            s_isDispatchingFrame = false;
            FlushPendingBoundsMoves();
        }
    }

    private static KeyValuePair<long, Action>[] GetFrameCallbackSnapshot()
    {
        if (!s_frameCallbackSnapshotDirty)
        {
            return s_frameCallbackSnapshot;
        }

        s_frameCallbackSnapshot = FrameCallbacks.ToArray();
        s_frameCallbackSnapshotDirty = false;
        return s_frameCallbackSnapshot;
    }

    private static void FlushPendingBoundsMoves()
    {
        if (PendingBoundsMoves.Count == 0)
        {
            return;
        }

        PendingBoundsMove[] moves = PendingBoundsMoves.Values.ToArray();
        PendingBoundsMoves.Clear();
        long started = Stopwatch.GetTimestamp();
        var succeeded = new bool[moves.Length];

        try
        {
            foreach (PendingBoundsMove move in moves) move.BeforeCommit();
            bool committed = TryCommitBatch(moves);
            if (committed)
            {
                Array.Fill(succeeded, true);
            }
            else
            {
                for (int i = 0; i < moves.Length; i++)
                {
                    PendingBoundsMove move = moves[i];
                    try
                    {
                        bool moved = Win32Helper.SetWindowPos(
                            move.WindowHandle, IntPtr.Zero, move.Bounds.X, move.Bounds.Y,
                            move.Bounds.Width, move.Bounds.Height, move.Flags);
                        if (!moved) move.Fallback();
                        succeeded[i] = true;
                    }
                    catch (Exception ex)
                    {
                        App.Log(
                            "[CompactBoundsBatch] Window commit failed: " + ex);
                    }
                }
            }
        }
        finally
        {
            // A native transaction belongs to all participating windows. Each
            // owner measures its preparation separately and receives one share
            // of this commit, excluding time queued behind other owners.
            double nativeWorkMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds / moves.Length;
            for (int i = 0; i < moves.Length; i++)
            {
                try { moves[i].AfterCommit(succeeded[i], nativeWorkMilliseconds); }
                catch (Exception ex)
                {
                    App.Log(
                        "[CompactBoundsBatch] Completion failed: " + ex);
                }
            }

            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsedMs >= 8)
            {
                string details = $"count={moves.Length} elapsedMs={elapsedMs:F1}";
                PerformanceLogger.Mark("CompactBoundsBatch", details);
                App.LogVerbose($"[CompactBoundsBatch] {details}");
            }
        }
    }

    private static bool TryCommitBatch(IReadOnlyList<PendingBoundsMove> moves)
    {
        IntPtr deferred = Win32Helper.BeginDeferWindowPos(moves.Count);
        if (deferred == IntPtr.Zero)
        {
            return false;
        }

        foreach (PendingBoundsMove move in moves)
        {
            IntPtr next = Win32Helper.DeferWindowPos(
                deferred,
                move.WindowHandle,
                IntPtr.Zero,
                move.Bounds.X,
                move.Bounds.Y,
                move.Bounds.Width,
                move.Bounds.Height,
                move.Flags);
            if (next == IntPtr.Zero)
            {
                // A failed DeferWindowPos invalidates the transaction. The
                // caller retries every real bounds update directly.
                return false;
            }

            deferred = next;
        }

        return Win32Helper.EndDeferWindowPos(deferred);
    }

    private static void Unregister(long registrationId)
    {
        if (FrameCallbacks.Remove(registrationId))
        {
            s_frameCallbackSnapshotDirty = true;
        }
        FrameTargets.Remove(registrationId);
        BoundsTransitionRegistrations.Remove(registrationId);
        if (FrameCallbacks.Count != 0 || !s_isRenderingSubscribed)
        {
            return;
        }

        App.NotifyMemoryCleanupActivity();
        StopFrameClock();
        s_isRenderingSubscribed = false;
        s_isClockPreArmed = false;
        s_preArmLingerTimer?.Stop();
        s_frameCallbackSnapshot = [];
        s_frameCallbackSnapshotDirty = false;
        s_clockBoostLease?.Dispose();
        s_clockBoostLease = null;
        WidgetAnimationDisplayTiming.Clear();
        s_lastFrameTickTimestamp = 0;
    }

    private static void StopFrameClock()
    {
        switch (s_clockSource)
        {
            case FrameClockSource.DwmFlushThread:
                if (s_dwmClockRun is { } run) run.IsActive = false;
                s_dwmClockRun = null;
                s_frameDispatcher = null;
                break;
            case FrameClockSource.RefreshTimer:
                StopRefreshTimer();
                s_frameDispatcher = null;
                break;
            case FrameClockSource.CompositionRendering:
                CompositionTarget.Rendering -= OnRendering;
                break;
        }

        s_clockSource = FrameClockSource.None;
    }

    private static void StopRefreshTimer()
    {
        s_refreshFramePending = false;
        if (s_refreshFrameTimer is not null)
        {
            s_refreshFrameTimer.Stop();
            s_refreshFrameTimer.Tick -= OnRefreshTimerTick;
            s_refreshFrameTimer = null;
        }
    }

    private sealed class Registration(long registrationId) : IDisposable
    {
        private long _registrationId = registrationId;

        public void Dispose()
        {
            long id = Interlocked.Exchange(ref _registrationId, 0);
            if (id != 0)
            {
                Unregister(id);
            }
        }
    }
}

internal static class WidgetCompactAnimationConcurrencyPolicy
{
    public static bool ShouldAnimate(int activeTransitions, int maximumConcurrentTransitions)
    {
        return maximumConcurrentTransitions > 0 &&
            activeTransitions >= 0 &&
            activeTransitions < maximumConcurrentTransitions;
    }
}
