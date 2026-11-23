using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticExpandedRestoreTests
{
    [Fact]
    public void FiveResizedMembersKeepTheirCurrentColumnsWhenCollapsedAndReopened()
    {
        var attachment = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2251, 0, 420, 600), ["middle"] = new(2679, 0, 549, 799),
            ["upper-right"] = new(3236, 0, 604, 576), ["lower-right"] = new(3236, 584, 580, 616),
            ["lower-middle"] = new(2808, 807, 420, 616)
        };
        var expanded = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2151, 118, 420, 600), ["middle"] = new(2579, 124, 337, 799),
            ["upper-right"] = new(3344, 118, 373, 576), ["lower-right"] = new(3344, 702, 373, 616),
            ["lower-middle"] = new(2916, 925, 420, 616)
        };
        var sizes = expanded.ToDictionary(pair => pair.Key, pair => new SizeInt32(pair.Value.Width, 78));
        IReadOnlyDictionary<string, RectInt32> homes = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2151, 118, 420, 78), ["middle"] = new(2656, 118, 337, 78),
            ["upper-right"] = new(2995, 118, 373, 78), ["lower-right"] = new(2995, 198, 373, 78),
            ["lower-middle"] = new(2573, 198, 420, 78)
        };
        for (int cycle = 0; cycle < 3; cycle++)
        {
            homes = WidgetMagneticCollapseLayout.RestoreCompact(expanded, homes, sizes, "RightTop");
            var origin = WidgetCoordinatedMoveCalculator.GetUnion(homes.Values);
            Assert.Equal(expanded, WidgetMagneticCollapseLayout.RestoreExpanded(expanded,
                new(origin.X, origin.Y), attachment, "RightTop"));
            Assert.Equal(homes["left"].X + homes["left"].Width + 2, homes["middle"].X);
            Assert.True(homes["lower-middle"].X >= homes["middle"].X + homes["middle"].Width);
            Assert.Equal(homes["upper-right"].X, homes["lower-right"].X);
            Assert.Equal(homes["upper-right"].Y + 80, homes["lower-right"].Y);
            Assert.Equal(homes["lower-middle"].X + homes["lower-middle"].Width + 2, homes["lower-right"].X);
            Assert.Equal(homes["lower-middle"].Y, homes["lower-right"].Y);
        }
    }

    [Fact]
    public void CompactionPreservesHorizontalClearanceBetweenSeparatedColumns()
    {
        var expanded = new Dictionary<string, RectInt32>
        {
            ["left"] = new(100, 100, 420, 600),
            ["right"] = new(800, 100, 373, 576),
            ["lower-right"] = new(800, 684, 373, 616)
        };
        var sizes = expanded.ToDictionary(pair => pair.Key, pair => new SizeInt32(pair.Value.Width, 78));
        var homes = WidgetMagneticCollapseLayout.RestoreCompact(expanded,
            new Dictionary<string, RectInt32>(), sizes);
        Assert.Equal(800, homes["right"].X);
        Assert.Equal(800, homes["lower-right"].X);
        Assert.Equal(180, homes["lower-right"].Y);
    }

    [Fact]
    public void RestorePreservesIndependentNeighborPositions()
    {
        var expanded = FourWidgets();
        var restored = WidgetMagneticCollapseLayout.RestoreExpanded(expanded, new(100, 100));
        Assert.Equal(expanded, restored);
        Assert.Equal(684, restored["lower-right"].Y);
        Assert.Equal(907, restored["lower-left"].Y);
    }

    [Theory]
    [InlineData(-600, -200)]
    [InlineData(200, 300)]
    public void MovingTheCompactOriginTranslatesAllExpandedMembersTogether(int x, int y)
    {
        var expanded = FourWidgets();
        var restored = WidgetMagneticCollapseLayout.RestoreExpanded(expanded, new(x, y));
        foreach (var pair in expanded)
        {
            Assert.Equal(pair.Value.X + x - 100, restored[pair.Key].X);
            Assert.Equal(pair.Value.Y + y - 100, restored[pair.Key].Y);
            Assert.Equal(pair.Value.Width, restored[pair.Key].Width);
            Assert.Equal(pair.Value.Height, restored[pair.Key].Height);
        }
    }

    [Fact]
    public void RepeatedCompactAndExpandedCyclesPreserveTheResizedColumn()
    {
        var expanded = FourWidgets();
        expanded["upper-left"] = new(100, 100, 549, 999);
        expanded["lower-left"] = new(253, 1107, 420, 616);
        var compactSizes = expanded.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, 78));
        var expected = expanded.ToDictionary();
        for (int cycle = 0; cycle < 4; cycle++)
        {
            var compact = WidgetMagneticCollapseLayout.ArrangeVisible(expanded, compactSizes, true);
            RectInt32 union = WidgetCoordinatedMoveCalculator.GetUnion(compact.Values);
            expanded = WidgetMagneticCollapseLayout.RestoreExpanded(expanded, new(union.X, union.Y)).ToDictionary();
            Assert.Equal(expected, expanded);
        }
    }

    [Fact]
    public void AttachmentReconciliationPreservesTheResizedIndependentColumn()
    {
        var attachment = FourWidgets();
        var expanded = FourWidgets();
        expanded["upper-left"] = new(100, 100, 549, 999);
        expanded["lower-left"] = new(253, 1107, 420, 616);
        var restored = WidgetMagneticCollapseLayout.RestoreExpanded(expanded, new(100, 100), attachment, "RightTop", 8, 18);
        Assert.Equal(expanded, restored);
        Assert.Equal(684, restored["lower-right"].Y);
    }

    [Fact]
    public void MovingOneCapsulePreservesItsContactSpacingOnExpansion()
    {
        var attachment = new Dictionary<string, RectInt32>
        { ["left"] = new(100, 100, 500, 600), ["right"] = new(610, 100, 400, 300), ["lower"] = new(610, 410, 400, 290) };
        var expanded = attachment.ToDictionary();
        expanded["left"] = new(110, 100, 500, 600);
        var restored = WidgetMagneticCollapseLayout.RestoreExpanded(expanded, new(110, 100), attachment, "LeftTop", 10, 12);
        Assert.Equal(110, restored["left"].X);
        Assert.Equal(620, restored["right"].X);
        Assert.Equal(620, restored["lower"].X);
        Assert.Equal(410, restored["lower"].Y);
    }

    [Fact]
    public void ClosingAVerticalGapInFiveWidgetGroupKeepsTopRowFlushAcrossCollapseCycles()
    {
        var attachment = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2168, 159, 420, 955),
            ["mid-top"] = new(2596, 159, 420, 627),
            ["mid-bottom"] = new(2596, 794, 420, 568),
            ["right-top"] = new(3024, 159, 664, 781),
            ["right-bottom"] = new(3024, 966, 664, 562)
        };
        var expanded = attachment.ToDictionary();
        // Grow right-bottom upward to close the 26px gap down to the 8px snap spacing.
        expanded["right-bottom"] = new(3024, 948, 664, 580);

        var sizes = expanded.ToDictionary(pair => pair.Key, pair => new SizeInt32(pair.Value.Width, 78));
        IReadOnlyDictionary<string, RectInt32> homes = new Dictionary<string, RectInt32>();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            homes = WidgetMagneticCollapseLayout.RestoreCompact(expanded, homes, sizes, "RightTop", 8, 18);
            var origin = WidgetCoordinatedMoveCalculator.GetUnion(homes.Values);
            var restored = WidgetMagneticCollapseLayout.RestoreExpanded(
                expanded, new(origin.X, origin.Y), attachment, "RightTop", 8, 18);
            Assert.Equal(expanded, restored);
            Assert.Equal(159, restored["left"].Y);
            Assert.Equal(159, restored["mid-top"].Y);
            Assert.Equal(159, restored["right-top"].Y);
            Assert.Equal(948, restored["right-bottom"].Y);
        }
    }

    [Fact]
    public void RealigningHigherTopWidgetDownFlushPreservesBothTopRowAndLowerNeighbor()
    {
        var attachment = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2168, 159, 420, 955),
            ["mid-top"] = new(2596, 159, 420, 627),
            ["mid-bottom"] = new(2596, 794, 420, 568),
            ["right-top"] = new(3024, 141, 664, 781),
            ["right-bottom"] = new(3024, 948, 664, 580)
        };
        var expanded = attachment.ToDictionary();
        // User aligns right-top down to y=159 and closes the 26px gap to 8px.
        expanded["right-top"] = new(3024, 159, 664, 781);

        var sizes = expanded.ToDictionary(pair => pair.Key, pair => new SizeInt32(pair.Value.Width, 78));
        var homes = WidgetMagneticCollapseLayout.RestoreCompact(
            expanded, new Dictionary<string, RectInt32>(), sizes, "RightTop", 8, 18);
        var origin = WidgetCoordinatedMoveCalculator.GetUnion(homes.Values);
        var restored = WidgetMagneticCollapseLayout.RestoreExpanded(
            expanded, new(origin.X, origin.Y), attachment, "RightTop", 8, 18);

        Assert.Equal(expanded, restored);
        Assert.Equal(159, restored["right-top"].Y);
        Assert.Equal(948, restored["right-bottom"].Y);
    }

    private static Dictionary<string, RectInt32> FourWidgets() => new()
    {
        ["upper-left"] = new(100, 100, 549, 799), ["upper-right"] = new(657, 100, 604, 576),
        ["lower-left"] = new(253, 907, 420, 616), ["lower-right"] = new(681, 684, 580, 616)
    };
}
