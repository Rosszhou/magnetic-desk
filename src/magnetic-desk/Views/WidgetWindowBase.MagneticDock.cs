using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    private bool _magneticDockCompleted;

    public void SetMagneticDockFeedback(MagneticDockEdge edge, double offset, double progress, bool completed, double animationTime)
    {
        string? style = App.Current?.WidgetManager?.ActiveMagneticAnimationStyle ?? Config.MagneticAnimationStyle;
        WidgetShellControl.SetMagneticDockFeedback(edge, offset, progress, completed, animationTime, style);
    }

    public void HideMagneticDockFeedback()
    {
        WidgetShellControl.HideMagneticDockFeedback();
    }

    public bool TryCompleteMagneticDock(RectInt32 bounds)
    {
        if (!IsDragging || _isCoordinatedMoveDrag || IsClosing || IsCompactBoundsStateActive)
        {
            return false;
        }
        _magneticDockCompleted = true;
        CancelPendingTitleBarDragFrame();
        App.Current?.ResizeGuideOverlay.EndDrag();
        ApplyWindowBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height, persist: false, updateConfig: false);
        CommitUserPlacementFromWindow(WidgetPlacementSource.Drag);
        return true;
    }
}
