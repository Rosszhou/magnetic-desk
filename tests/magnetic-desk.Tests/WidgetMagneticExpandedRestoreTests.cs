using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticExpandedRestoreTests
{
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

    private static Dictionary<string, RectInt32> FourWidgets() => new()
    {
        ["upper-left"] = new(100, 100, 549, 799), ["upper-right"] = new(657, 100, 604, 576),
        ["lower-left"] = new(253, 907, 420, 616), ["lower-right"] = new(681, 684, 580, 616)
    };
}
