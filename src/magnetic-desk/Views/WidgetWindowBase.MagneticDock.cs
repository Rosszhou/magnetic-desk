using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    private bool _magneticDockCompleted;

    internal RectInt32 MagneticDockBounds
    {
        get
        {
            if (!IsCompactBoundsStateActive) return GetActualWindowBounds();
            RectInt32 expanded = Config.MagneticExpandedPlacement?.ToBounds() ?? ResolvePersistedExpandedHostBounds();
            RectInt32 compact = GetActualWindowBounds();
            var layout = ResolveCompactExpansionLayout(compact, new SizeInt32(expanded.Width, expanded.Height), requireFullSize: true);
            return layout.CanExpand ? layout.ExpandedBounds : new(compact.X, compact.Y, expanded.Width, expanded.Height);
        }
    }

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
        if (!IsDragging || _isCoordinatedMoveDrag || IsClosing)
        {
            return false;
        }
        _magneticDockCompleted = true;
        CancelPendingTitleBarDragFrame();
        App.Current?.ResizeGuideOverlay.EndDrag();
        if (IsCompactBoundsStateActive) ApplyMagneticCollapsedState(false, bounds, persist: false, animate: false);
        ApplyWindowBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height, persist: false, updateConfig: false);
        CommitUserPlacementFromWindow(WidgetPlacementSource.Drag);
        return true;
    }
}
