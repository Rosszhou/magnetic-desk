using MagneticDesk.Models;
using Windows.Graphics;

namespace MagneticDesk.Services;

/// <summary>Only geometry and magnetic presentation are restored. Widget content stays independently editable.</summary>
internal sealed record WidgetLayoutState(
    double X,
    double Y,
    double Width,
    double Height,
    string? PositionAnchor,
    double PositionMarginX,
    double PositionMarginY,
    string? PositionMonitorKey,
    string? PositionMonitorDeviceName,
    string? PositionMonitorStableId,
    bool? PositionMonitorWasPrimary,
    int BoundsCoordinateVersion,
    WidgetScreenBindingMode ScreenBindingMode,
    string? BoundScreenId,
    string? BoundScreenLabel,
    bool IsCollapsed,
    double? CompactWidth,
    string? MagneticGroupId,
    bool MagneticCollapseLinked,
    string? MagneticAttachmentAnchor,
    string? CapsuleStyle,
    CompactLayoutState? CompactPlacement,
    RectInt32? Expanded, RectInt32? Attachment, RectInt32? Compact,
    string? CollapseBehavior, string? IndependentCollapse)
{
    internal static WidgetLayoutState Capture(WidgetConfig config) => new(
        config.X,
        config.Y,
        config.Width,
        config.Height,
        config.PositionAnchor,
        config.PositionMarginX,
        config.PositionMarginY,
        config.PositionMonitorKey,
        config.PositionMonitorDeviceName,
        config.PositionMonitorStableId,
        config.PositionMonitorWasPrimary,
        config.BoundsCoordinateVersion,
        config.ScreenBindingMode,
        config.BoundScreenId,
        config.BoundScreenLabel,
        config.IsCollapsed,
        config.CompactWidth,
        config.MagneticGroupId,
        config.MagneticCollapseLinked,
        config.MagneticAttachmentAnchor,
        config.CapsuleStyle,
        CompactLayoutState.Capture(config.CompactPlacement),
        config.MagneticExpandedPlacement?.ToBounds(),
        config.MagneticAttachmentPlacement?.ToBounds(),
        config.MagneticCompactPlacement?.ToBounds(),
        config.Metadata.GetValueOrDefault(WidgetCollapseBehaviorNames.MetadataKey),
        config.Metadata.GetValueOrDefault(WidgetManager.MagneticIndependentCollapseKey));

    internal void Restore(WidgetConfig config)
    {
        config.X = X;
        config.Y = Y;
        config.Width = Width;
        config.Height = Height;
        config.PositionAnchor = PositionAnchor;
        config.PositionMarginX = PositionMarginX;
        config.PositionMarginY = PositionMarginY;
        config.PositionMonitorKey = PositionMonitorKey;
        config.PositionMonitorDeviceName = PositionMonitorDeviceName;
        config.PositionMonitorStableId = PositionMonitorStableId;
        config.PositionMonitorWasPrimary = PositionMonitorWasPrimary;
        config.BoundsCoordinateVersion = BoundsCoordinateVersion;
        config.ScreenBindingMode = ScreenBindingMode;
        config.BoundScreenId = BoundScreenId;
        config.BoundScreenLabel = BoundScreenLabel;
        config.IsCollapsed = IsCollapsed;
        config.CompactWidth = CompactWidth;
        config.MagneticGroupId = MagneticGroupId;
        config.MagneticCollapseLinked = MagneticCollapseLinked;
        config.MagneticAttachmentAnchor = MagneticAttachmentAnchor;
        config.CapsuleStyle = CapsuleStyle;

        config.CompactPlacement = CompactPlacement?.ToPlacement();
        config.MagneticExpandedPlacement = Expanded is { } expanded ? MagneticExpandedPlacement.FromBounds(expanded) : null;
        config.MagneticAttachmentPlacement = Attachment is { } attachment ? MagneticExpandedPlacement.FromBounds(attachment) : null;
        config.MagneticCompactPlacement = Compact is { } compact ? MagneticExpandedPlacement.FromBounds(compact) : null;
        if (CollapseBehavior is null) config.Metadata.Remove(WidgetCollapseBehaviorNames.MetadataKey);
        else config.Metadata[WidgetCollapseBehaviorNames.MetadataKey] = CollapseBehavior;
        if (IndependentCollapse is null) config.Metadata.Remove(WidgetManager.MagneticIndependentCollapseKey);
        else config.Metadata[WidgetManager.MagneticIndependentCollapseKey] = IndependentCollapse;
    }
}

internal sealed record CompactLayoutState(double X, double Y, string? PositionAnchor, double PositionMarginX, double PositionMarginY, string? PositionMonitorKey, string? PositionMonitorDeviceName, string? PositionMonitorStableId, bool? PositionMonitorWasPrimary, int BoundsCoordinateVersion)
{
    internal static CompactLayoutState? Capture(WidgetCompactPlacement? placement) => placement is null ? null : new(
        placement.X, placement.Y, placement.PositionAnchor, placement.PositionMarginX, placement.PositionMarginY, placement.PositionMonitorKey, placement.PositionMonitorDeviceName, placement.PositionMonitorStableId, placement.PositionMonitorWasPrimary, placement.BoundsCoordinateVersion);

    internal WidgetCompactPlacement ToPlacement() => new()
    {
        X = X,
        Y = Y,
        PositionAnchor = PositionAnchor,
        PositionMarginX = PositionMarginX,
        PositionMarginY = PositionMarginY,
        PositionMonitorKey = PositionMonitorKey,
        PositionMonitorDeviceName = PositionMonitorDeviceName,
        PositionMonitorStableId = PositionMonitorStableId,
        PositionMonitorWasPrimary = PositionMonitorWasPrimary,
        BoundsCoordinateVersion = BoundsCoordinateVersion,
    };
}
