using MagneticDesk.Models;
using Windows.Graphics;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    internal int ResizeRejectionFeedbackCount { get; private set; }

    internal void ShowResizeRejectedFeedback()
    {
        ResizeRejectionFeedbackCount++;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(RootElement, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootElement);
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(280);
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(.15f, -5);
        animation.InsertKeyFrame(.35f, 5);
        animation.InsertKeyFrame(.55f, -4);
        animation.InsertKeyFrame(.75f, 3);
        animation.InsertKeyFrame(1, 0);
        visual.StartAnimation("Translation.X", animation);
    }

    internal void ApplyMagneticResizeBounds(RectInt32 bounds)
    {
        _magneticLayoutVisibleBounds = bounds;
        PrepareCoordinatedMoveBounds(bounds);
        try { ApplyCoordinatedMoveBoundsFallback(bounds); }
        finally { CompleteCoordinatedMoveBoundsPreview(); }
    }

    internal RectInt32 ResolveMagneticExpandedResizeBounds(RectInt32 expanded, RectInt32 actual)
    {
        if (!IsCompactBoundsStateActive) return actual;
        return new RectInt32(expanded.X, expanded.Y, UsesAlignedCompactWidth() ? actual.Width : expanded.Width, expanded.Height);
    }

    internal void CommitMagneticResizeLayout(RectInt32 expanded)
    {
        RectInt32 actual = GetActualWindowBounds();
        CapturePositionAnchor(expanded.X, expanded.Y, expanded.Width, expanded.Height);
        UpdateConfigBoundsFromPhysical(expanded.X, expanded.Y, expanded.Width, expanded.Height, persist: false);
        if (IsCompactBoundsStateActive)
        {
            CaptureCompactPlacement(actual, persist: false);
            _magneticLayoutMoveOrigin = actual;
        }
        else
        {
            _compactArrangementSizeOverride = null;
            RecaptureCompactPlacementAfterExpandedResize(expanded);
            _magneticLayoutMoveOrigin = actual;
        }
        Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(expanded);
        Config.MagneticCompactPlacement = IsCompactBoundsStateActive
            ? MagneticExpandedPlacement.FromBounds(actual) : null;
        SynchronizeWidgetGroupLayout();
    }
}
