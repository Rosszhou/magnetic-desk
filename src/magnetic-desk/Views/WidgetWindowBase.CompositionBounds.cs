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
    private RectInt32? _compositionHostBounds;
    private IDisposable? _compositionBoundsStartRegistration;
    private bool _compositionBoundsPending;
    private (double Width, double Height, HorizontalAlignment Horizontal, VerticalAlignment Vertical) _compositionRootLayout;
    internal RectInt32 RestingBoundsForLayout => IsCompactTransitionActive ? _collapseAnimationTo : GetActualWindowBounds();

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
        RectInt32 host = WidgetCoordinatedMoveCalculator.GetUnion([from, to]);
        _compositionHostBounds = host;
        _compositionBoundsPending = true;
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
            state.InsertVector2("FromOffset", new Vector2((float)((from.X - host.X) / dpiScale), (float)((from.Y - host.Y) / dpiScale)));
            state.InsertVector2("ToOffset", new Vector2((float)((to.X - host.X) / dpiScale), (float)((to.Y - host.Y) / dpiScale)));
            var visual = ElementCompositionPreview.GetElementVisual(RootElement);
            var translation = state.Compositor.CreateExpressionAnimation(
                "Vector3(state.FromOffset.X + (state.ToOffset.X - state.FromOffset.X) * state.Progress, state.FromOffset.Y + (state.ToOffset.Y - state.FromOffset.Y) * state.Progress, 0)");
            translation.SetReferenceParameter("state", state);
            visual.Properties.StartAnimation("Translation", translation);
            translation.Dispose();
            MoveWindowWithoutPersisting(host, committed: _ =>
            {
                if (generation != _collapseAnimationGeneration || _compositionHostBounds is null) return;
                if (!ApplyCompositionWindowClip(from)) CompleteBoundsTransitionImmediately("region-failed");
            }, deferUntilPrepared: true);
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
        _compositionBoundsStartRegistration = WidgetCompactAnimationCoordinator.StartOnPreparedFrame(timestamp =>
        {
            if (generation != _collapseAnimationGeneration || IsClosing) return;
            _compositionBoundsStartRegistration?.Dispose();
            _compositionBoundsStartRegistration = null;
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

    private bool ApplyCompositionWindowClip(RectInt32 bounds)
    {
        RectInt32 host = _compositionHostBounds!.Value;
        return Win32Helper.SetAnimationWindowClip(HWnd,
            new RectInt32(bounds.X - host.X, bounds.Y - host.Y, bounds.Width, bounds.Height));
    }

    private void EndCompositionBounds(RectInt32? settle = null)
    {
        if (_compositionHostBounds is null) return;
        _compositionBoundsStartRegistration?.Dispose();
        _compositionBoundsStartRegistration = null;
        RectInt32 visible = settle ?? CompositionVisibleBounds();
        _compositionHostBounds = null;
        _compositionBoundsPending = false;
        if (!IsClosing && !BoundsEqual(GetActualWindowBounds(), visible)) MoveWindowWithoutPersisting(visible);
        Win32Helper.ClearAnimationWindowClip(HWnd);
        var visual = ElementCompositionPreview.GetElementVisual(RootElement);
        visual.Properties.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        WidgetShellControl.EndCompositionBounds();
        (RootElement.Width, RootElement.Height, RootElement.HorizontalAlignment, RootElement.VerticalAlignment) = _compositionRootLayout;
    }
}
