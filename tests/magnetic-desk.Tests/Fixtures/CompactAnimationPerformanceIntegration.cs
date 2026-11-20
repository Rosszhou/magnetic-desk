#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Reflection;
using System.Text.Json;
using MagneticDesk.Controls;
using MagneticDesk.Models;
using MagneticDesk.Views;
using Windows.Graphics;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using MagneticDesk.Platform;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task MeasureCompactAnimationAsync()
    {
        if (!MagneticDeskDataPathService.Current.IsDevelopmentRoot)
            throw new InvalidOperationException("Animation measurement requires an isolated development profile.");
        string root = MagneticDeskDataPathService.Current.RootPath;
        var samples = new List<object>();
        var checks = new List<string>();
        var renderSamples = new List<object>();
        WidgetShell[] observedShells = [];
        SizeChangedEventHandler[] sizeObservers = [];
        int[] sizeChanges = [];
        int renderFrames = 0;
        long lastRender = 0;
        double maximumRenderGap = 0;
        void OnRendered(object? sender, object args)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastRender != 0)
                maximumRenderGap = Math.Max(maximumRenderGap, Stopwatch.GetElapsedTime(lastRender, now).TotalMilliseconds);
            lastRender = now;
            renderFrames++;
        }
        var trackerField = typeof(WidgetWindowBase).GetField("_compactAnimationFrameTracker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lastUpdateField = typeof(WidgetCompactAnimationFrameTracker).GetField("_lastBoundsUpdateTimestamp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var hostField = typeof(WidgetWindowBase).GetField("_compositionHostBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool nativeBounds = Environment.GetEnvironmentVariable("MAGNETIC_DESK_COMPACT_NATIVE_BOUNDS") == "1";
        bool realLayout = Environment.GetEnvironmentVariable("MAGNETIC_DESK_ANIMATION_REAL_LAYOUT") == "1";
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        try
        {
            await Task.Delay(2000);
            var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().OrderBy(window => window.Config.Id).ToArray();
            Check(windows.Length == 6, "six native file windows loaded");
            var shellProperty = typeof(WidgetWindowBase).GetProperty("WidgetShellControl", BindingFlags.Instance | BindingFlags.NonPublic)!;
            observedShells = windows.Select(window => (WidgetShell)shellProperty.GetValue(window)!).ToArray();
            sizeChanges = new int[windows.Length];
            sizeObservers = new SizeChangedEventHandler[windows.Length];
            for (int index = 0; index < windows.Length; index++)
            {
                int slot = index;
                sizeObservers[index] = (_, _) => sizeChanges[slot]++;
                observedShells[index].SizeChanged += sizeObservers[index];
            }
            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                window.RequestCompactState(false, false);
                var bounds = realLayout ? window.CoordinatedMoveBounds : new RectInt32(100 + i % 3 * 510, 100 + i / 3 * 560, 500, 550);
                window.ApplyMagneticResizeBounds(bounds);
                window.CommitMagneticResizeLayout(bounds);
                window.Config.MagneticGroupId = "animation-performance";
                window.Config.MagneticCollapseLinked = true;
                window.Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(bounds);
                window.Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(bounds);
                window.Config.MagneticCompactPlacement = null;
                window.Config.MagneticAttachmentAnchor = "LeftTop";
                WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Smart);
                window.RefreshMagneticCollapseBehavior();
            }
            _mouseLeaveTimer?.Stop();
            foreach (int participants in new[] { 6, 1 })
            {
                if (participants == 1)
                {
                    DissolveMagneticGroup(windows[0].Config);
                    foreach (var window in windows.Skip(1))
                    {
                        WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Expanded);
                        window.RefreshMagneticCollapseBehavior();
                    }
                    _mouseLeaveTimer?.Stop();
                }
                var animated = windows.Take(participants).ToArray();
                long cleanupEpoch = App.MemoryCleanupEpoch;
                for (int step = 0; step < (participants == 6 ? 20 : 10); step++)
                {
                    bool collapsed = step % 2 == 0;
                    renderFrames = 0;
                    lastRender = 0;
                    maximumRenderGap = 0;
                    // Native resize needs a XAML frame cadence sample. The
                    // independent path must stay free of a per-frame observer.
                    if (nativeBounds) CompositionTarget.Rendering += OnRendered;
                    windows[0].RequestCompactState(collapsed, true);
                    _mouseLeaveTimer?.Stop();
                    var physical = animated.Select(window => ReadPhysicalBounds(window)).ToArray();
                    await Task.Delay(40);
                    Array.Clear(sizeChanges);
                    await Task.Delay(160);
                    var middleSizeChanges = sizeChanges.ToArray();
                    var trackers = animated.Select(window => trackerField.GetValue(window) as WidgetCompactAnimationFrameTracker).ToArray();
                    Check(trackers.All(tracker => tracker is not null), "all group members animate " + step);
                    if (!nativeBounds)
                    {
                        var independent = animated.Select(window => !WidgetBackgroundCustomization.HasCustomBackground(window.Config)).ToArray();
                        Check(animated.Select((window, index) => (hostField.GetValue(window) is RectInt32) == independent[index]).All(value => value),
                            "members select the expected animation pipeline " + step);
                        Check(animated.Select((window, index) => !independent[index] || ReadPhysicalBounds(window).Equals(physical[index])).All(value => value),
                            "composition host size stays fixed mid-transition " + step);
                        Check(independent.Select((enabled, index) => !enabled || middleSizeChanges[index] == 0).All(value => value),
                            "composition shell layout size stays fixed mid-transition " + step);
                        var startField = typeof(WidgetWindowBase).GetField("_collapseAnimationStarted", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        Check(animated.Select(window => (long)startField.GetValue(window)!).Distinct().Count() == 1,
                            "members share the same animation start " + step);
                    }
                    string? clock = typeof(WidgetCompactAnimationCoordinator)
                        .GetField("s_clockSource", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)?.ToString();
                    await Task.Delay(200);
                    CompositionTarget.Rendering -= OnRendered;
                    renderSamples.Add(new { step, participants, collapsed, renderFrames, maximumRenderGap });
                    await Task.Delay(250);
                    Check(animated.All(window => window.IsCompactArrangementActive == collapsed && !window.MagneticBoundsTransitionActive),
                        "group transition settles " + step);
                    for (int i = 0; i < trackers.Length; i++)
                    {
                        var tracker = trackers[i]!;
                        long finished = (long)lastUpdateField.GetValue(tracker)!;
                        var summary = tracker.Complete(finished);
                        samples.Add(new { step, participants, clock, measured = step >= 4, collapsed, widget = animated[i].Config.Id,
                            summary.RefreshRateHz, summary.FrameCount, summary.BoundsUpdateCount,
                            summary.ElapsedMilliseconds, summary.EstimatedDroppedFrames, MidTransitionLayoutChanges = middleSizeChanges[i],
                            summary.MaximumFrameIntervalMilliseconds, summary.MaximumBoundsUpdateIntervalMilliseconds,
                            summary.MaximumSubmissionWorkMilliseconds });
                    }
                }
                for (int step = 0; step < 6; step++)
                {
                    windows[0].RequestCompactState(step % 2 == 0, true);
                    _mouseLeaveTimer?.Stop();
                    await Task.Delay(90);
                    Check(animated.All(window => Win32Helper.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window))),
                        "all animated surfaces remain visible during reversal " + participants + "/" + step);
                }
                windows[0].RequestCompactState(false, true);
                await Task.Delay(650);
                Check(animated.All(window => !window.MagneticBoundsTransitionActive && !window.IsCompactArrangementActive),
                    "all reversals settle expanded " + participants);
                Check(App.MemoryCleanupEpoch == cleanupEpoch, "continuous animation keeps memory warm " + participants);
            }
            Check(windows.All(window => !window.IsCompactArrangementActive), "all windows end expanded");
            var shell = (WidgetShell)shellProperty.GetValue(windows[0])!;
            for (int step = 0; step < 8; step++)
            {
                bool collapsed = step % 2 == 0;
                windows[0].RequestCompactState(collapsed, true);
                _mouseLeaveTimer?.Stop();
                await Task.Delay(90);
                Check(Win32Helper.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(windows[0])), "native surface remains visible during reversal " + step);
                Check(shell.Visibility == Visibility.Visible && shell.Opacity > 0 && shell.ActualWidth > 0 && shell.ActualHeight > 0,
                    "live shell remains visible during reversal " + step);
            }
            windows[0].RequestCompactState(false, true);
            await Task.Delay(650);
            Check(!windows[0].MagneticBoundsTransitionActive && !windows[0].IsCompactArrangementActive,
                "rapid reversals settle expanded");
            // Render the real shell at an intermediate size. This covers XAML
            // background painting separately from native bounds submissions.
            foreach (bool collapsed in new[] { true, false })
            {
                windows[0].RequestCompactState(collapsed, true);
                _mouseLeaveTimer?.Stop();
                await Task.Delay(110);
                Check(windows[0].MagneticBoundsTransitionActive, "background probe starts during native animation " + collapsed);
                if (!nativeBounds)
                {
                    var state = (Microsoft.UI.Composition.CompositionPropertySet)typeof(WidgetShell)
                        .GetField("_compositionBoundsProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                    Check(state.TryGetAnimationController("Progress") is not null, "geometry runs on compositor " + collapsed);
                    var host = (RectInt32)hostField.GetValue(windows[0])!;
                    var visible = windows[0].CoordinatedMoveBounds;
                    IntPtr region = CreateRectRgn(0, 0, 0, 0);
                    try
                    {
                        Check(GetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(windows[0]), region) > 0,
                            "native input region exists during animation " + collapsed);
                        Check(PtInRegion(region, visible.X - host.X + visible.Width / 2, visible.Y - host.Y + visible.Height / 2),
                            "visible center accepts input " + collapsed);
                        Check(!PtInRegion(region, host.Width + 8, host.Height + 8), "outside host rejects input " + collapsed);
                        int outsideY = visible.Y - host.Y > 16 ? 4 : host.Height - 4;
                        Check(!PtInRegion(region, host.Width / 2, outsideY), "hidden part of physical host rejects input " + collapsed);
                    }
                    finally { DeleteObject(region); }
                }
                await Task.Delay(650);
            }
            foreach (var window in windows)
            {
                var windowShell = (WidgetShell)shellProperty.GetValue(window)!;
                var clip = (Microsoft.UI.Composition.RectangleClip)typeof(WidgetShell)
                    .GetField("_capsuleClipGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windowShell)!;
                Check(Math.Abs(clip.Right - windowShell.ActualWidth) < 0.01 && Math.Abs(clip.Bottom - windowShell.ActualHeight) < 0.01,
                    "composition clip matches settled native size " + window.Config.Id);
                windowShell.SetCapsuleStyle(nameof(WidgetCapsuleStyle.Fill));
                windowShell.SetMouseLeaveCountdown(true, 0.5);
                Check(Math.Abs(((Microsoft.UI.Xaml.Controls.Border)windowShell.FindName("CountdownFill")).Width - windowShell.ActualWidth * 0.5) < 0.01,
                    "visible fill updates after hidden countdown " + window.Config.Id);
                windowShell.SetMouseLeaveCountdown(false, 1);
                windowShell.SetCapsuleStyle(nameof(WidgetCapsuleStyle.Segments));
                Check(((Microsoft.UI.Xaml.Controls.Grid)windowShell.FindName("TitleBarGrid")).Padding.Right == 12,
                    "style rebuild preserves hidden countdown padding " + window.Config.Id);
            }
            var source = windows[0];
            source.RequestCompactState(true, true);
            await Task.Delay(90);
            var targetField = typeof(WidgetWindowBase).GetField("_collapseAnimationTo", BindingFlags.Instance | BindingFlags.NonPublic)!;
            RectInt32 resting = (RectInt32)targetField.GetValue(source)!;
            BeginLayoutOperation(source.Config.Id, "Magnetic.Undo.Move");
            source.BeginCoordinatedMoveParticipation(isSource: false);
            source.ApplyMagneticResizeBounds(new(resting.X + 70, resting.Y, resting.Width, resting.Height));
            source.CompleteCoordinatedMoveParticipation(hasMoved: true, isSource: false);
            CompleteLayoutOperation(source.Config.Id);
            Check(UndoLayout(), "drag during compact animation remains undoable");
            Check(source.CoordinatedMoveBounds.Equals(resting), "undo restores resting bounds after animation interruption");
            source.RequestCompactState(false, false);
            _mouseLeaveTimer?.Stop();
            if (Environment.GetEnvironmentVariable("MAGNETIC_DESK_COMPOSITION_CAPTURE") == "1")
            {
                // Expose one isolated test window for the desktop capture tool.
                var window = windows[0];
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                window.Title = "Magnetic Desk animation sample";
                long style = Win32Helper.GetWindowLongPtr(handle, Win32Helper.GWL_EXSTYLE).ToInt64();
                Win32Helper.SetWindowLongPtr(handle, Win32Helper.GWL_EXSTYLE,
                    new IntPtr((style & ~Win32Helper.WS_EX_TOOLWINDOW) | 0x00040000));
                Win32Helper.SetWindowLongPtr(handle, Win32Helper.GWLP_HWNDPARENT, IntPtr.Zero);
                window.RequestCompactState(true, false);
                window.RequestCompactState(false, true);
                await Task.Delay(90);
                var state = (Microsoft.UI.Composition.CompositionPropertySet)typeof(WidgetShell)
                    .GetField("_compositionBoundsProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                var controller = state.TryGetAnimationController("Progress")!;
                controller.Pause();
                controller.Progress = 0.2f;
                typeof(WidgetWindowBase).GetField("_compositionBoundsPending", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                ((IDisposable?)typeof(WidgetWindowBase).GetField("_collapseAnimationWatchdogTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))?.Dispose();
                Win32Helper.ClearAnimationWindowClip(handle);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "animation-performance-passed.json"),
                JsonSerializer.Serialize(new { checks, samples, renderSamples, passed = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "animation-performance-failed.json"),
                JsonSerializer.Serialize(new { checks, samples, error = ex.ToString() }));
        }
        finally
        {
            for (int index = 0; index < observedShells.Length; index++)
                observedShells[index].SizeChanged -= sizeObservers[index];
            CompositionTarget.Rendering -= OnRendered;
            _mouseLeaveTimer?.Stop();
        }
    }

    private static RectInt32 ReadPhysicalBounds(WidgetWindowBase window)
    {
        Win32Helper.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window), out var bounds);
        return new RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr region);
}
#endif
