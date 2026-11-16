using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    private RectInt32? _magneticTransitionBounds;
    private RectInt32? _magneticLayoutMoveOrigin;
    private RectInt32? _magneticLayoutVisibleBounds;

    internal bool MagneticBoundsTransitionActive => IsCompactTransitionActive || _isBoundsInteractionActive || IsDragging || IsResizing;
    internal bool MagneticCollapseInitialized => _collapseInitialized && !IsClosing;
    internal bool MagneticSmartPinnedOpen => _isSmartPinnedOpen;
    internal RectInt32 MagneticWorkArea => ResolveCompactWorkArea(GetCurrentWindowBounds());
    internal bool MagneticGroupInteractionActive =>
        _isBoundsInteractionActive || _compactInteractionDepth > 0 ||
        HasBlockingFlyoutOpen() || IsPointerPhysicallyInsideWindow();

    internal RectInt32 MagneticExpandedBounds =>
        IsCompactBoundsStateActive
            ? Config.MagneticExpandedPlacement?.ToBounds() ?? ResolvePersistedExpandedHostBounds()
            : GetCurrentWindowBounds();

    internal SizeInt32 MagneticCompactSize
    {
        get
        {
            var expanded = MagneticExpandedBounds;
            var bounds = WidgetCompactBoundsCalculator.Resolve(Config, expanded,
                WidgetPositioningService.GetDpiScale(ResolveCompactWorkArea(expanded)),
                ResolveEffectiveCompactContentMode(), alignToExpandedWidth: UsesAlignedCompactWidth(),
                heightContentMode: SettingsService.Settings.WidgetCompactContentMode);
            return new SizeInt32(bounds.Width, bounds.Height);
        }
    }

    internal void ResetMagneticInteractionState()
    {
        CancelTimer(ref _collapseHoverTimer);
        _isSmartPinnedOpen = false;
        _requirePointerExitBeforeHoverExpansion = true;
        _suppressSmartExpansionUntilPointerExit = true;
        _magneticLayoutMoveOrigin = GetActualWindowBounds();
        _magneticLayoutVisibleBounds = GetActualWindowBounds();
    }

    internal void RefreshMagneticCollapseBehavior()
    {
        _isSmartPinnedOpen = false;
        ApplyCompactTooltips();
        ApplyCollapseBehaviorVisuals();
    }

    internal void DetachMagneticCollapseLayout()
    {
        if (IsCompactBoundsStateActive || Config.MagneticExpandedPlacement is null) return;
        _compactArrangementSizeOverride = null;
        RecaptureCompactPlacementAfterExpandedResize(GetCurrentWindowBounds());
        Config.MagneticExpandedPlacement = null;
        _magneticLayoutMoveOrigin = null;
        _magneticLayoutVisibleBounds = null;
    }

    internal void ApplyMagneticCollapsedState(bool collapsed, RectInt32 bounds, bool persist, bool animate, bool pinnedOpen = false)
    {
        _magneticTransitionBounds = bounds;
        _isSmartPinnedOpen = !collapsed && pinnedOpen;
        try
        {
            SetCollapsedState(collapsed, persist, animate,
                allowDuringInteraction: true, bypassExpansionReadiness: true);
            _magneticLayoutVisibleBounds = bounds;
            if (collapsed) _magneticLayoutMoveOrigin = bounds;
            else
            {
                _magneticLayoutMoveOrigin = bounds;
                var expanded = Config.MagneticExpandedPlacement?.ToBounds() ?? bounds;
                CapturePositionAnchor(expanded.X, expanded.Y, expanded.Width, expanded.Height);
                UpdateConfigBoundsFromPhysical(expanded.X, expanded.Y, expanded.Width, expanded.Height, persist: false);
                if (!Config.MagneticCollapseLinked && App.Current?.WidgetManager?.HasMagneticGroup(Config) != true)
                {
                    RecaptureCompactPlacementAfterExpandedResize(bounds);
                    Config.MagneticExpandedPlacement = null;
                }
            }
        }
        finally
        {
            _magneticTransitionBounds = null;
        }
    }

    internal void ApplyMagneticReflowBounds(RectInt32 bounds, bool animate = false)
    {
        _magneticLayoutVisibleBounds = bounds;
        if (BoundsEqual(GetActualWindowBounds(), bounds) && !IsCompactTransitionActive) return;
        if (IsCompactBoundsStateActive) _stableCompactBounds = bounds;
        StartBoundsTransition(GetActualWindowBounds(), bounds, IsWidgetCollapsed,
            animate ? ResolveCompactTransitionDuration(null) : 0);
        _magneticLayoutVisibleBounds = bounds;
        _magneticLayoutMoveOrigin = bounds;
        if (IsCompactBoundsStateActive) CaptureCompactPlacement(bounds, persist: false);
    }

    private void UpdateMagneticExpandedPlacementAfterMove(RectInt32 actual)
    {
        if (Config.MagneticExpandedPlacement is not { } expanded) return;
        RectInt32 before = _magneticLayoutMoveOrigin ?? (IsCompactBoundsStateActive ? GetStableCompactBounds(actual) : expanded.ToBounds());
        if (Config.MagneticCompactPlacement is { } compact)
        {
            compact.X += actual.X - before.X;
            compact.Y += actual.Y - before.Y;
        }
        expanded.X += actual.X - before.X;
        expanded.Y += actual.Y - before.Y;
        if (!IsCompactBoundsStateActive)
        {
            expanded.Width = actual.Width;
            expanded.Height = actual.Height;
        }
        _magneticLayoutMoveOrigin = actual;
        _magneticLayoutVisibleBounds = actual;
        RectInt32 restored = expanded.ToBounds();
        RectInt32 workArea = ResolveCompactWorkArea(actual);
        WidgetPositioningService.CaptureAnchor(Config, CollapseHostBoundsToContent(restored), workArea);
        WidgetPositioningService.UpdateConfigFromPhysicalBounds(Config, restored, workArea);
    }
}
