using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticLayoutExperienceTests
{
    [Fact]
    public void DockOutlineUsesPhysicalBoundsOnNegativeCoordinateMonitors()
    {
        var outline = WidgetLayoutGuides.Outline(new(-800, 100, 600, 400), 1.5);
        Assert.Equal(4, outline.Count);
        Assert.Equal(new RectInt32(-800, 100, 600, 3), outline[0].Bounds);
        Assert.Equal(new RectInt32(-203, 100, 3, 400), outline[3].Bounds);
    }

    [Fact]
    public void AlignmentLineSpansBothWindows()
    {
        var match = new WidgetSnapMatch(WidgetSnapEdge.Left, WidgetSnapEdge.Left, 100, 100, (IntPtr)2, false, 0);
        var result = new WidgetMoveSnapResult(new(100, 400, 200, 200), match, null);
        var guides = WidgetLayoutGuides.Drag(result, [new(new(100, 0, 250, 200), (IntPtr)2)], null, 1);
        Assert.Contains(guides, g => g.Bounds.Equals(new RectInt32(100, 0, 1, 600)));
    }

    [Fact]
    public void DragGuidesOnlyShowAlignmentLines()
    {
        var result = new WidgetMoveSnapResult(new(0, 0, 200, 200), null, null);
        Assert.Empty(WidgetLayoutGuides.Drag(result, [new(new(220, 0, 200, 200), (IntPtr)2)], null, 1));
        var match = new WidgetSnapMatch(WidgetSnapEdge.Top, WidgetSnapEdge.Top, 0, 0, (IntPtr)2, false, 0);
        var guides = WidgetLayoutGuides.Drag(result with { VerticalMatch = match },
            [new(new(220, 0, 200, 200), (IntPtr)2)], null, 1);
        Assert.Single(guides);
        Assert.All(guides, guide => Assert.True(guide.Bounds.Width == 1 || guide.Bounds.Height == 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlignmentLineStaysInsideRightAndBottomEdges(bool vertical)
    {
        var edge = vertical ? WidgetSnapEdge.Right : WidgetSnapEdge.Bottom;
        var source = new RectInt32(0, 0, 300, 200);
        var match = new WidgetSnapMatch(edge, edge, vertical ? 300 : 200, 0, IntPtr.Zero, false, 0);
        var result = new WidgetMoveSnapResult(source, vertical ? match : null, vertical ? null : match);
        var guides = WidgetLayoutGuides.Drag(result, [], source, 1.5);
        var line = Assert.Single(guides).Bounds;
        Assert.Equal(vertical ? 298 : 198, vertical ? line.X : line.Y);
        Assert.Equal(2, vertical ? line.Width : line.Height);
    }

    [Fact]
    public void SnapshotDeepCopiesPlacementAndPreservesLaterContentEdits()
    {
        var config = new WidgetConfig
        {
            MagneticGroupId = "group", MagneticExpandedPlacement = new() { X = 100, Width = 400 },
            CompactPlacement = new() { X = 200, PositionMonitorStableId = "display" }, CompactWidth = 250
        };
        config.Metadata[WidgetCollapseBehaviorNames.MetadataKey] = "Smart";
        var snapshot = WidgetLayoutState.Capture(config);
        config.MagneticExpandedPlacement.X = 900;
        config.CompactPlacement.X = 800;
        config.Metadata["Notes"] = "edited after moving";
        config.Metadata[WidgetManager.MagneticLayoutLockMetadataKey] = "group";
        config.Name = "renamed";
        snapshot.Restore(config);
        Assert.Equal(100, config.MagneticExpandedPlacement!.X);
        Assert.Equal(200, config.CompactPlacement!.X);
        Assert.Equal("display", config.CompactPlacement.PositionMonitorStableId);
        Assert.Equal("renamed", config.Name);
        Assert.Equal("edited after moving", config.Metadata["Notes"]);
        Assert.Equal("group", config.Metadata[WidgetManager.MagneticLayoutLockMetadataKey]);
        Assert.Equal(snapshot, WidgetLayoutState.Capture(config));
    }

    [Fact]
    public void SnapshotRestoresLegacyNullPlacements()
    {
        var config = new WidgetConfig();
        var snapshot = WidgetLayoutState.Capture(config);
        config.MagneticGroupId = "new-group";
        config.MagneticAttachmentPlacement = new() { Width = 500 };
        config.Metadata[WidgetCollapseBehaviorNames.MetadataKey] = "Click";
        snapshot.Restore(config);
        Assert.Null(config.MagneticGroupId);
        Assert.Null(config.MagneticAttachmentPlacement);
        Assert.False(config.Metadata.ContainsKey(WidgetCollapseBehaviorNames.MetadataKey));
    }
}
