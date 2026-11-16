using MagneticDesk.Services;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    internal void RefreshCapsuleAppearance()
    {
        WidgetShellControl.SetCapsuleStyle(Config.CapsuleStyle);
        ApplySurfaceStyle();
        ApplyWidgetForegroundAppearance();
        ApplyWindowCornerPreference();
    }
    internal void ShowMouseLeaveCountdown(bool visible, double remainingFraction) =>
        WidgetShellControl.SetMouseLeaveCountdown(visible, remainingFraction);

    internal bool CanCollapseAfterMouseLeave => _collapseInitialized && !IsClosing &&
        EffectiveCollapseBehavior is WidgetCollapseBehavior.Click or WidgetCollapseBehavior.Smart;

    internal bool MouseLeaveScopeHasActivity => _isBoundsInteractionActive || IsDragging || IsResizing ||
        _isDisplayTopologyTransitionActive || _isCompactDragInside || _compactInteractionDepth > 0 ||
        HasBlockingFlyoutOpen() || IsCompactTransitionActive || IsPointerRoutedToThisWindow();

    internal void CollapseAfterMouseLeave()
    {
        _isSmartPinnedOpen = false;
        SetCollapsedState(true, persistManualState: EffectiveCollapseBehavior == WidgetCollapseBehavior.Click,
            animate: true, allowDuringInteraction: true);
    }
}
