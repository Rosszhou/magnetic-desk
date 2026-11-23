using System.Diagnostics;
using System.Numerics;
using MagneticDesk.Models;
using MagneticDesk.Platform;
using MagneticDesk.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    private const double AnimationClipCoalesceMinIntervalMs = 42;
    private const int AnimationClipCoalesceMinDeltaPx = 6;
    private const double ExpandClipMinLookaheadMs = 56;

    private RectInt32? _compositionHostBounds;
    private bool _compositionHostFollowsBounds;
    private IDisposable? _compositionBoundsFrameRegistration;
    private bool _compositionBoundsPending;
    private RectInt32? _lastAnimationWindowClip;
    private long _lastAnimationWindowClipTimestamp;
    private int _animationClipUpdateCount;
    private RectInt32? _deferredCollapseHostBounds;
    private RectInt32 _deferredCollapseTargetBounds;
    private long _deferredCollapseGeneration;
    private IDisposable? _deferredCollapsePresentRegistration;
    private IDisposable? _deferredCollapseSettleRegistration;
    private OwnedOneShotDispatcherTimer? _deferredCollapseSettleWatchdog;
    private IDisposable? _deferredCollapseClipClearRegistration;
    private OwnedOneShotDispatcherTimer? _deferredCollapseClipClearWatchdog;
    private int _deferredCollapseClipClearFrames;
    private (double Width, double Height, HorizontalAlignment Horizontal, VerticalAlignment Vertical) _compositionRootLayout;
    internal RectInt32 RestingBoundsForLayout =>
        IsCompactTransitionActive ? _collapseAnimationTo :
        _deferredCollapseHostBounds is not null ? _deferredCollapseTargetBounds :
        GetActualWindowBounds();

    private bool TryPrepareCompositionBounds(RectInt32 from, RectInt32 to, bool collapsed, double dpiScale, long generation)
    {
#if MAGNETIC_DESK_MAGNETIC_PREVIEW
        if (MagneticDeskDataPathService.Current.IsDevelopmentRoot &&
            Environment.GetEnvironmentVariable("MAGNETIC_DESK_COMPACT_NATIVE_BOUNDS") == "1") return false;
#endif
        // Legacy background overrides retain their existing layout path.
        // The independent shape background covers the standard capsule styles.
        if (!WidgetShellControl.SupportsCompositionBounds || WidgetBackgroundCustomization.HasCustomBackground(Config) ||
            WidgetBackgroundCustomization.IsGlobalImageBackgroundActive(SettingsService.Settings)) return false;
        CancelDeferredCollapseClipClear(clearNow: false);
        RectInt32 host = WidgetCoordinatedMoveCalculator.GetUnion([from, to]);
        bool followsExpansion = !collapsed && !BoundsEqual(host, to);
        bool followsCollapse = collapsed && from.Width >= to.Width && from.Height >= to.Height;
        bool followsBounds = followsExpansion || followsCollapse;
        if (followsExpansion)
        {
            // Keep the expanded client size from the first frame. Moving this
            // host during expansion avoids rebasing its region and visual
            // coordinates on different DWM frames at the end.
            if (from.Width > to.Width || from.Height > to.Height) return false;
            host = new RectInt32(from.X, from.Y, to.Width, to.Height);
        }
        else if (followsCollapse)
        {
            // Keep the already-allocated expanded HWND unchanged on frame 0 so
            // collapsing never triggers giant Union(from, to) swapchain reallocations;
            // slide (X, Y) via position-only SWP_NOSIZE during collapse instead.
            host = from;
        }
        _compositionHostBounds = host;
        _compositionHostFollowsBounds = followsBounds;
        _compositionBoundsPending = true;
        _lastAnimationWindowClip = null;
        _lastAnimationWindowClipTimestamp = 0;
        _animationClipUpdateCount = 0;
        _compositionRootLayout = (RootElement.Width, RootElement.Height, RootElement.HorizontalAlignment, RootElement.VerticalAlignment);
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(RootElement, true);
            ElementCompositionPreview.GetElementVisual(RootElement).Properties.InsertVector3("Translation", Vector3.Zero);
            float fromRadius = (float)WidgetCapsuleAppearance.CornerRadius(
                WidgetCapsuleAppearance.Normalize(Config.CapsuleStyle), !collapsed);
            float toRadius = (float)WidgetCapsuleAppearance.CornerRadius(
                WidgetCapsuleAppearance.Normalize(Config.CapsuleStyle), collapsed);
            RootElement.Width = Math.Max(from.Width, to.Width) / dpiScale;
            RootElement.Height = Math.Max(from.Height, to.Height) / dpiScale;
            RootElement.HorizontalAlignment = HorizontalAlignment.Left;
            RootElement.VerticalAlignment = VerticalAlignment.Top;
            var state = WidgetShellControl.PrepareCompositionBounds(
                new Vector2((float)(from.Width / dpiScale), (float)(from.Height / dpiScale)),
                new Vector2((float)(to.Width / dpiScale), (float)(to.Height / dpiScale)),
                fromRadius, toRadius,
                _collapseAnimationAnchor ?? WidgetCompactExpansionAnchor.LeftTop, collapsed,
                _collapseAnimationVisualProfile with { DurationMilliseconds = _collapseAnimationDurationMs });
            if (!followsBounds)
            {
                state.InsertVector2("FromOffset", new Vector2((float)((from.X - host.X) / dpiScale), (float)((from.Y - host.Y) / dpiScale)));
                state.InsertVector2("ToOffset", new Vector2((float)((to.X - host.X) / dpiScale), (float)((to.Y - host.Y) / dpiScale)));
                var visual = ElementCompositionPreview.GetElementVisual(RootElement);
                using var translation = state.Compositor.CreateExpressionAnimation(
                    "Vector3(state.FromOffset.X + (state.ToOffset.X - state.FromOffset.X) * state.Progress, state.FromOffset.Y + (state.ToOffset.Y - state.FromOffset.Y) * state.Progress, 0)");
                translation.SetReferenceParameter("state", state);
                visual.Properties.StartAnimation("Translation", translation);
            }
            else if (!ApplyCompositionWindowClip(from))
            {
                EndCompositionBounds(settle: from);
                return false;
            }
            if (BoundsEqual(GetActualWindowBounds(), host))
            {
                if (!followsBounds && !ApplyCompositionWindowClip(from))
                {
                    EndCompositionBounds(settle: from);
                    return false;
                }
            }
            else
            {
                MoveWindowWithoutPersisting(host, committed: _ =>
                {
                    if (generation != _collapseAnimationGeneration || _compositionHostBounds is null) return;
                    if (!followsBounds && !ApplyCompositionWindowClip(from)) CompleteBoundsTransitionImmediately("region-failed");
                }, deferUntilPrepared: true);
            }
            RootElement.UpdateLayout();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[CompactComposition] Preparation failed {Config.Id}: {ex}");
            EndCompositionBounds(settle: from);
            return false;
        }
    }

    private void QueueBoundsTransitionStart(long generation, bool compositionBounds)
    {
        _compositionBoundsPending = true;
        _compositionBoundsFrameRegistration = WidgetCompactAnimationCoordinator.StartOnPreparedFrame(timestamp =>
        {
            if (generation != _collapseAnimationGeneration || IsClosing) return;
            _compositionBoundsFrameRegistration?.Dispose();
            _compositionBoundsFrameRegistration = null;
            _collapseAnimationStarted = timestamp;
            _compositionBoundsPending = false;
            _collapseAnimationPacing.Reset(0, WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd));
            _compactAnimationFrameTracker = new WidgetCompactAnimationFrameTracker(timestamp, Win32Helper.GetDisplayRefreshRateForWindow(HWnd));
            try
            {
                if (compositionBounds) WidgetShellControl.StartCompositionBounds();
                else WidgetShellControl.StartPreparedCompactVisuals(_targetCollapsed);
            }
            catch (Exception ex)
            {
                App.Log($"[CompactComposition] Start failed {Config.Id}: {ex}");
                CompleteBoundsTransitionAfterTimeout();
            }
        });
    }

    private RectInt32 CompositionVisibleBounds()
    {
        if (_compositionBoundsPending) return _collapseAnimationFrom;
        double elapsed = Stopwatch.GetElapsedTime(_collapseAnimationStarted).TotalMilliseconds;
        double progress = _collapseAnimationVisualProfile.EaseProgress(Math.Clamp(elapsed / _collapseAnimationDurationMs, 0, 1));
        return _collapseAnimationAnchor is { } anchor
            ? WidgetCompactExpansionCalculator.InterpolateAnchoredBounds(_collapseAnimationFrom, _collapseAnimationTo, _collapseAnimationPivot, anchor, progress)
            : InterpolateBounds(_collapseAnimationFrom, _collapseAnimationTo, progress);
    }

    private RectInt32 ResolveLocalAnimationWindowClip(RectInt32 bounds, bool allowLookahead, double elapsedMs, double frameBudgetMs)
    {
        RectInt32 host = _compositionHostBounds!.Value;
        RectInt32 currentLocal = _compositionHostFollowsBounds
            ? new RectInt32(0, 0, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height))
            : new RectInt32(bounds.X - host.X, bounds.Y - host.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        if (!allowLookahead || _targetCollapsed || frameBudgetMs <= 0) return currentLocal;

        double effectiveBudget = double.IsFinite(frameBudgetMs) && frameBudgetMs > 0
            ? frameBudgetMs
            : WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
        double clipIntervalMs = Math.Max(AnimationClipCoalesceMinIntervalMs, effectiveBudget * 3);
        double lookaheadMs = Math.Max(ExpandClipMinLookaheadMs, clipIntervalMs + effectiveBudget * 1.5);
        double lookaheadProgress = Math.Clamp((elapsedMs + lookaheadMs) / Math.Max(1, _collapseAnimationDurationMs), 0, 1);
        double lookaheadEased = _collapseAnimationVisualProfile.EaseProgress(lookaheadProgress);
        RectInt32 projected = _collapseAnimationAnchor is { } anchor
            ? WidgetCompactExpansionCalculator.InterpolateAnchoredBounds(_collapseAnimationFrom, _collapseAnimationTo, _collapseAnimationPivot, anchor, lookaheadEased)
            : InterpolateBounds(_collapseAnimationFrom, _collapseAnimationTo, lookaheadEased);

        if (_compositionHostFollowsBounds)
        {
            int maxWidth = host.Width - currentLocal.Width > 8 ? host.Width - 6 : host.Width;
            int maxHeight = host.Height - currentLocal.Height > 8 ? host.Height - 6 : host.Height;
            int width = Math.Min(Math.Max(currentLocal.Width, projected.Width), Math.Max(currentLocal.Width, maxWidth));
            int height = Math.Min(Math.Max(currentLocal.Height, projected.Height), Math.Max(currentLocal.Height, maxHeight));
            return new RectInt32(0, 0, Math.Max(1, width), Math.Max(1, height));
        }

        int currentRight = currentLocal.X + currentLocal.Width;
        int currentBottom = currentLocal.Y + currentLocal.Height;
        int projLeft = projected.X - host.X;
        int projTop = projected.Y - host.Y;
        int projRight = projLeft + projected.Width;
        int projBottom = projTop + projected.Height;
        int minLeft = currentLocal.X > 8 ? 6 : 0;
        int minTop = currentLocal.Y > 8 ? 6 : 0;
        int maxRight = host.Width - currentRight > 8 ? host.Width - 6 : host.Width;
        int maxBottom = host.Height - currentBottom > 8 ? host.Height - 6 : host.Height;
        int left = Math.Max(minLeft, Math.Min(currentLocal.X, projLeft));
        int top = Math.Max(minTop, Math.Min(currentLocal.Y, projTop));
        int right = Math.Min(maxRight, Math.Max(currentRight, projRight));
        int bottom = Math.Min(maxBottom, Math.Max(currentBottom, projBottom));
        return new RectInt32(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private bool ApplyCompositionWindowClip(RectInt32 bounds, out bool appliedNativeClip, bool force = false, double elapsedMs = 0, double frameBudgetMs = 0)
    {
        appliedNativeClip = false;
        RectInt32 host = _compositionHostBounds!.Value;
        RectInt32 currentLocal = _compositionHostFollowsBounds
            ? new RectInt32(0, 0, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height))
            : new RectInt32(bounds.X - host.X, bounds.Y - host.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        long now = Stopwatch.GetTimestamp();
        if (!force && _lastAnimationWindowClip is { } previous)
        {
            bool expansionWouldClip = !_targetCollapsed && (
                currentLocal.X < previous.X ||
                currentLocal.Y < previous.Y ||
                currentLocal.X + currentLocal.Width > previous.X + previous.Width ||
                currentLocal.Y + currentLocal.Height > previous.Y + previous.Height);
            if (!expansionWouldClip)
            {
                // Scale coalescing and per-HWND phase stagger from the active display's
                // live frame budget so 60Hz / 120Hz / 144Hz / 240Hz / 360Hz all spread
                // SetWindowRgn calls across distinct VSync frames.
                double effectiveBudget = double.IsFinite(frameBudgetMs) && frameBudgetMs > 0
                    ? frameBudgetMs
                    : WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
                double clipIntervalMs = Math.Max(AnimationClipCoalesceMinIntervalMs, effectiveBudget * 3);
                double staggerMs = _animationClipUpdateCount <= 1
                    ? (HWnd.ToInt64() & 7) * Math.Clamp(effectiveBudget, 4, 17)
                    : 0;
                double sinceLastMs = Stopwatch.GetElapsedTime(_lastAnimationWindowClipTimestamp, now).TotalMilliseconds;
                if (sinceLastMs < clipIntervalMs + staggerMs) return true;
                int maxDelta = Math.Max(
                    Math.Max(Math.Abs(currentLocal.X - previous.X), Math.Abs(currentLocal.Y - previous.Y)),
                    Math.Max(Math.Abs(currentLocal.Width - previous.Width), Math.Abs(currentLocal.Height - previous.Height)));
                if (maxDelta < AnimationClipCoalesceMinDeltaPx) return true;
            }
        }
        RectInt32 localClip = ResolveLocalAnimationWindowClip(bounds, allowLookahead: !force, elapsedMs, frameBudgetMs);
        if (!Win32Helper.SetAnimationWindowClip(HWnd, localClip)) return false;
        _lastAnimationWindowClip = localClip;
        _lastAnimationWindowClipTimestamp = now;
        _animationClipUpdateCount++;
        appliedNativeClip = true;
        return true;
    }

    private bool ApplyCompositionWindowClip(RectInt32 bounds, bool force = false, double elapsedMs = 0, double frameBudgetMs = 0) =>
        ApplyCompositionWindowClip(bounds, out _, force, elapsedMs, frameBudgetMs);

    private bool ApplyCompositionWindowFrame(RectInt32 bounds, long generation, out bool submittedNativeWork, bool forceClip = false,
        double elapsedMs = 0, double frameBudgetMs = 0)
    {
        submittedNativeWork = false;
        if (!ApplyCompositionWindowClip(bounds, out bool appliedClip, force: forceClip, elapsedMs: elapsedMs, frameBudgetMs: frameBudgetMs)) return false;
        submittedNativeWork = appliedClip;
        if (!_compositionHostFollowsBounds || _compositionHostBounds is not { } host ||
            (host.X == bounds.X && host.Y == bounds.Y)) return true;

        var destination = new RectInt32(bounds.X, bounds.Y, host.Width, host.Height);
        MoveWindowWithoutPersisting(destination, committed: _ =>
        {
            if (generation == _collapseAnimationGeneration && _compositionHostBounds is not null)
                _compositionHostBounds = destination;
        }, commitFailed: () =>
        {
            if (generation == _collapseAnimationGeneration)
                CompleteBoundsTransitionImmediately("host-move-failed");
        }, positionOnly: true);
        submittedNativeWork = true;
        return true;
    }

    private bool ApplyCompositionWindowFrame(RectInt32 bounds, long generation, bool forceClip = false,
        double elapsedMs = 0, double frameBudgetMs = 0) =>
        ApplyCompositionWindowFrame(bounds, generation, out _, forceClip, elapsedMs, frameBudgetMs);

    private int ResolveSettledCapsuleCornerRadiusPx(double dpiScale)
    {
        double radiusDip = WidgetCapsuleAppearance.CornerRadius(
            WidgetCapsuleAppearance.Normalize(Config.CapsuleStyle), compact: true);
        return Math.Max(0, (int)Math.Round(radiusDip * Math.Max(0.01, dpiScale)));
    }

    private bool QueueCompositionTransitionFinish(RectInt32 bounds, long generation, Action finish)
    {
        if (_compositionHostBounds is null) return false;
        if ((!_targetCollapsed || _compositionHostFollowsBounds) &&
            !ApplyCompositionWindowFrame(bounds, generation, forceClip: true)) return false;
        if (_targetCollapsed && _compositionHostBounds is { } host)
        {
            double dpiScale = Math.Max(0.01, Win32Helper.GetDpiScaleForWindow(HWnd, RootElement.XamlRoot));
            RectInt32 localTarget = _compositionHostFollowsBounds
                ? new RectInt32(0, 0, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height))
                : new RectInt32(bounds.X - host.X, bounds.Y - host.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
            Win32Helper.SetRoundedAnimationWindowClip(HWnd, localTarget, ResolveSettledCapsuleCornerRadiusPx(dpiScale));
        }
        WidgetShellControl.SetFinalCompositionBoundsFrame();
        double frameBudget = WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
        if (_targetCollapsed)
        {
            // Stagger Phase 1 (EndCompositionBounds + SetCollapsed + UpdateLayout) at most
            // 1 widget per VSync frame while the GPU visual is already frozen at Progress=1.
            _compositionBoundsFrameRegistration = WidgetCompactAnimationCoordinator.EnqueueDeferredBoundsSettle(Complete);
            int fallback = Math.Max(200, (int)Math.Ceiling(14 * frameBudget));
            ScheduleTimer(ref _collapseAnimationWatchdogTimer, fallback, Complete);
        }
        else
        {
            _compositionBoundsFrameRegistration = WidgetCompactAnimationCoordinator.StartOnPreparedFrame(_ => Complete());
            int fallback = Math.Max(16, (int)Math.Ceiling(2 * frameBudget));
            ScheduleTimer(ref _collapseAnimationWatchdogTimer, fallback, Complete);
        }
        return true;

        void Complete()
        {
            if (generation != _collapseAnimationGeneration || IsClosing || _compositionBoundsFrameRegistration is null) return;
            _compositionBoundsFrameRegistration.Dispose();
            _compositionBoundsFrameRegistration = null;
            CancelTimer(ref _collapseAnimationWatchdogTimer);
            finish();
        }
    }

    /// <summary>
    /// Phase 1 of two-phase collapse settle: switches to the real 26px pill capsule
    /// inside the unchanged transparent host HWND (zero WM_SIZE / swapchain churn),
    /// then waits 1 presented frame before enqueueing a 1-window-per-VSync transparent
    /// host shrink in Phase 2 while a rounded pill HRGN masks any Win32/DWM corner flash.
    /// </summary>
    private bool TryBeginTwoPhaseCollapseSettle(long generation, Action completeLogicalCollapse)
    {
        if (_compositionHostBounds is not { } host || IsClosing) return false;
        RectInt32 target = _collapseAnimationTo;
        if (BoundsEqual(host, target)) return false;

        long phase1Started = Stopwatch.GetTimestamp();
        double dpiScale = Math.Max(0.01, Win32Helper.GetDpiScaleForWindow(HWnd, RootElement.XamlRoot));
        bool followsBounds = _compositionHostFollowsBounds;
        RectInt32 localTarget = followsBounds
            ? new RectInt32(0, 0, Math.Max(1, target.Width), Math.Max(1, target.Height))
            : new RectInt32(target.X - host.X, target.Y - host.Y, Math.Max(1, target.Width), Math.Max(1, target.Height));
        Win32Helper.SetRoundedAnimationWindowClip(HWnd, localTarget, ResolveSettledCapsuleCornerRadiusPx(dpiScale));

        _compositionBoundsFrameRegistration?.Dispose();
        _compositionBoundsFrameRegistration = null;
        _compositionHostBounds = null;
        _compositionHostFollowsBounds = false;
        _compositionBoundsPending = false;
        _lastAnimationWindowClip = null;
        _lastAnimationWindowClipTimestamp = 0;
        _animationClipUpdateCount = 0;

        float offsetX = followsBounds ? 0f : (float)((target.X - host.X) / dpiScale);
        float offsetY = followsBounds ? 0f : (float)((target.Y - host.Y) / dpiScale);
        RootElement.Width = target.Width / dpiScale;
        RootElement.Height = target.Height / dpiScale;
        RootElement.HorizontalAlignment = HorizontalAlignment.Left;
        RootElement.VerticalAlignment = VerticalAlignment.Top;
        var rootVisual = ElementCompositionPreview.GetElementVisual(RootElement);
        rootVisual.Properties.StopAnimation("Translation");
        rootVisual.Properties.InsertVector3("Translation", new Vector3(offsetX, offsetY, 0));

        WidgetShellControl.EndCompositionBounds(useDestinationSize: true, keepDestinationSizeLocked: true);

        _deferredCollapseHostBounds = host;
        _deferredCollapseTargetBounds = target;
        _deferredCollapseGeneration = generation;

        // Finish logical collapse (SetCollapsed, UpdateLayout at compact size) inside
        // the unchanged host HWND so the real 26px pill renders before any HWND resize.
        completeLogicalCollapse();

        double frameBudget = WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
        double phase1Ms = Stopwatch.GetElapsedTime(phase1Started).TotalMilliseconds;
        if (phase1Ms > frameBudget)
        {
            App.Log($"[CompactCollapseSettle] Phase1 slow id={Config.Id} phase1Ms={phase1Ms:F1} budgetMs={frameBudget:F1}");
        }

        if (_deferredCollapseHostBounds is null || IsClosing) return true;

        if (followsBounds && host.X == target.X && host.Y == target.Y)
        {
            // Keep the already-allocated transparent expanded HWND at (target.X, target.Y)
            // with the rounded pill HRGN passing all input outside the capsule through.
            // Avoiding SetWindowPos(WM_SIZE) completely eliminates WinUI 3 / DWM swapchain
            // reallocation (and its 1-frame square DWMWCP_DONOTROUND flash) on collapse
            // and makes the next hover-expand zero-WM_SIZE as well.
            ScheduleDeferredCollapseShieldDispose(generation);
            if (_targetCollapsed && generation == _collapseAnimationGeneration)
            {
                ScheduleExpandedLayerRestoreAfterFrameCommit(generation);
            }
            return true;
        }

        // Fallback for non-following host offsets: wait 1 XAML/DComp frame so the real
        // pill capsule is presented by DWM, then shrink 1 window per VSync frame.
        _deferredCollapsePresentRegistration = WidgetCompactAnimationCoordinator.StartOnPreparedFrame(_ =>
        {
            _deferredCollapsePresentRegistration?.Dispose();
            _deferredCollapsePresentRegistration = null;
            if (_deferredCollapseHostBounds is null || _deferredCollapseGeneration != generation || IsClosing) return;
            _deferredCollapseSettleRegistration = WidgetCompactAnimationCoordinator.EnqueueDeferredBoundsSettle(() =>
            {
                if (_deferredCollapseGeneration == generation)
                    CommitDeferredCollapseHostSettle(moveWindow: !IsClosing);
            });
        });

        int watchdogMs = Math.Max(220, (int)Math.Ceiling(16 * frameBudget));
        ScheduleTimer(ref _deferredCollapseSettleWatchdog, watchdogMs, () =>
        {
            if (_deferredCollapseGeneration == generation)
                CommitDeferredCollapseHostSettle(moveWindow: !IsClosing);
        });
        return true;
    }

    private void ReleaseDeferredCollapseHostForExpansion()
    {
        if (_deferredCollapseHostBounds is null) return;
        CommitDeferredCollapseHostSettle(moveWindow: false, immediateClipClear: true);
    }

    private void FlushDeferredCollapseHostSettle(bool moveWindow = true)
    {
        if (_deferredCollapseHostBounds is null)
        {
            CancelDeferredCollapseClipClear(clearNow: true);
            return;
        }
        CommitDeferredCollapseHostSettle(moveWindow: moveWindow && !IsClosing, immediateClipClear: true);
    }

    private void CommitDeferredCollapseHostSettle(bool moveWindow, bool immediateClipClear = false)
    {
        if (_deferredCollapseHostBounds is not { } host) return;
        long phase2Started = Stopwatch.GetTimestamp();
        RectInt32 target = _deferredCollapseTargetBounds;
        long generation = _deferredCollapseGeneration;
        _deferredCollapseHostBounds = null;
        _deferredCollapseGeneration = 0;
        _deferredCollapsePresentRegistration?.Dispose();
        _deferredCollapsePresentRegistration = null;
        _deferredCollapseSettleRegistration?.Dispose();
        _deferredCollapseSettleRegistration = null;
        CancelTimer(ref _deferredCollapseSettleWatchdog);
        CancelDeferredCollapseClipClear(clearNow: false);

        var rootVisual = ElementCompositionPreview.GetElementVisual(RootElement);
        rootVisual.Properties.StopAnimation("Translation");
        rootVisual.Properties.InsertVector3("Translation", Vector3.Zero);

        bool willShrinkWindow = moveWindow && !IsClosing && !BoundsEqual(GetActualWindowBounds(), target);
        if (willShrinkWindow && !immediateClipClear)
        {
            // Keep a rounded pill HRGN across SetWindowPos so Windows/DWM can never
            // flash a square DWMWCP_DONOTROUND redirection surface during WM_SIZE.
            if (host.X != target.X || host.Y != target.Y)
            {
                double dpiScale = Math.Max(0.01, Win32Helper.GetDpiScaleForWindow(HWnd, RootElement.XamlRoot));
                Win32Helper.SetRoundedAnimationWindowClip(
                    HWnd,
                    new RectInt32(0, 0, Math.Max(1, target.Width), Math.Max(1, target.Height)),
                    ResolveSettledCapsuleCornerRadiusPx(dpiScale));
            }
        }
        else
        {
            Win32Helper.ClearAnimationWindowClip(HWnd);
        }

        // Shrink the transparent outer HWND margin WHILE RootElement and WidgetShell
        // are still locked to the exact compact capsule size (zero stretch/clip jump).
        // Omit SWP_NOCOPYBITS so Windows preserves the already-rendered 26px pill bits
        // at (0, 0) across the swapchain resize instead of blinking a blank frame.
        if (willShrinkWindow)
        {
            MoveWindowWithoutPersisting(target, sizeOnly: host.X == target.X && host.Y == target.Y);
        }

        (RootElement.Width, RootElement.Height, RootElement.HorizontalAlignment, RootElement.VerticalAlignment) = _compositionRootLayout;
        bool deferClipAndShieldClear = willShrinkWindow && !immediateClipClear && !IsClosing;
        WidgetShellControl.ReleaseLockedDestinationSize(releaseShield: !deferClipAndShieldClear);
        if (!IsClosing)
        {
            WidgetShellControl.RefreshCapsuleClip();
            if (deferClipAndShieldClear)
            {
                ScheduleDeferredCollapseClipClear(generation);
            }
            if (_targetCollapsed && generation == _collapseAnimationGeneration)
            {
                ScheduleExpandedLayerRestoreAfterFrameCommit(generation);
            }
        }

        double frameBudget = WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
        double phase2Ms = Stopwatch.GetElapsedTime(phase2Started).TotalMilliseconds;
        if (phase2Ms > frameBudget)
        {
            App.Log($"[CompactCollapseSettle] Phase2 slow id={Config.Id} phase2Ms={phase2Ms:F1} budgetMs={frameBudget:F1}");
        }
    }

    private void ScheduleDeferredCollapseShieldDispose(long generation)
    {
        CancelDeferredCollapseClipClear(clearNow: false);
        _deferredCollapseClipClearFrames = 0;
        _deferredCollapseClipClearRegistration = WidgetCompactAnimationCoordinator.Register(() =>
        {
            if (IsClosing || generation != _collapseAnimationGeneration)
            {
                CancelDeferredCollapseClipClear(clearNow: false);
                return;
            }
            if (++_deferredCollapseClipClearFrames >= 2)
            {
                CancelDeferredCollapseClipClear(clearNow: false);
            }
        }, HWnd);
        double frameBudget = WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
        int fallbackMs = Math.Max(40, (int)Math.Ceiling(4 * frameBudget));
        ScheduleTimer(ref _deferredCollapseClipClearWatchdog, fallbackMs, () =>
        {
            CancelDeferredCollapseClipClear(clearNow: false);
        });
    }

    private void ScheduleDeferredCollapseClipClear(long generation)
    {
        CancelDeferredCollapseClipClear(clearNow: false);
        _deferredCollapseClipClearFrames = 0;
        _deferredCollapseClipClearRegistration = WidgetCompactAnimationCoordinator.Register(() =>
        {
            if (IsClosing || generation != _collapseAnimationGeneration)
            {
                CancelDeferredCollapseClipClear(clearNow: !IsClosing && _compositionHostBounds is null);
                return;
            }
            if (++_deferredCollapseClipClearFrames >= 2)
            {
                CancelDeferredCollapseClipClear(clearNow: true);
            }
        }, HWnd);
        double frameBudget = WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd);
        int fallbackMs = Math.Max(40, (int)Math.Ceiling(4 * frameBudget));
        ScheduleTimer(ref _deferredCollapseClipClearWatchdog, fallbackMs, () =>
        {
            CancelDeferredCollapseClipClear(clearNow: !IsClosing && _compositionHostBounds is null);
        });
    }

    private void CancelDeferredCollapseClipClear(bool clearNow)
    {
        bool wasPending = _deferredCollapseClipClearRegistration is not null || _deferredCollapseClipClearWatchdog is not null;
        _deferredCollapseClipClearRegistration?.Dispose();
        _deferredCollapseClipClearRegistration = null;
        CancelTimer(ref _deferredCollapseClipClearWatchdog);
        _deferredCollapseClipClearFrames = 0;
        if (wasPending)
        {
            WidgetShellControl.DisposeCompositionBoundsBackground();
        }
        if (clearNow && wasPending && !IsClosing)
        {
            Win32Helper.ClearAnimationWindowClip(HWnd);
        }
    }

    private void EndCompositionBounds(RectInt32? settle = null)
    {
        if (_compositionHostBounds is null) return;
        CancelDeferredCollapseClipClear(clearNow: false);
        _compositionBoundsFrameRegistration?.Dispose();
        _compositionBoundsFrameRegistration = null;
        RectInt32 visible = settle ?? CompositionVisibleBounds();
        _compositionHostBounds = null;
        _compositionHostFollowsBounds = false;
        _compositionBoundsPending = false;
        _lastAnimationWindowClip = null;
        _lastAnimationWindowClipTimestamp = 0;
        _animationClipUpdateCount = 0;
        if (!IsClosing && !BoundsEqual(GetActualWindowBounds(), visible)) MoveWindowWithoutPersisting(visible);
        var visual = ElementCompositionPreview.GetElementVisual(RootElement);
        visual.Properties.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        WidgetShellControl.EndCompositionBounds(useDestinationSize: settle.HasValue && BoundsEqual(visible, _collapseAnimationTo));
        (RootElement.Width, RootElement.Height, RootElement.HorizontalAlignment, RootElement.VerticalAlignment) = _compositionRootLayout;
        Win32Helper.ClearAnimationWindowClip(HWnd);
    }
}
