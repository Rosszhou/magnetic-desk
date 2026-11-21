using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticResizeLayoutTests
{
    [Theory]
    [InlineData(783, 535)]
    [InlineData(400, 535)]
    public void UpperLeftCanShortenWithoutMovingTheOtherColumn(int height, int lowerY)
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(70, 0, 547, 525), ["b"] = new(626, 0, 602, 525),
            ["c"] = new(223, 535, 417, 614), ["d"] = new(651, 535, 578, 614)
        };
        var result = Arrange(original, "a", new(70, 0, 547, height));
        Assert.Equal(original["b"], result["b"]);
        Assert.Equal(original["d"], result["d"]);
        Assert.Equal(lowerY, result["c"].Y);
        Assert.Equal(original["c"].X, result["c"].X);
    }

    [Fact]
    public void ShrinkingOneOfTwoSupportsKeepsTheSpanningMemberAboveTheOther()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 610, 300)
        };
        var result = Arrange(original, "a", new(0, 0, 300, 200));
        Assert.Equal(original["c"], result["c"]);
    }

    [Fact]
    public void ShorteningFillsAnUnblockedColumnWithoutPushingAnotherWindow()
    {
        var original = new Dictionary<string, RectInt32>
        { ["a"] = new(0, 0, 300, 400), ["b"] = new(0, 410, 300, 400), ["c"] = new(0, 820, 300, 400) };
        RectInt32 proposed = new(0, 0, 300, 200);
        var clear = Arrange(original, "a", proposed);
        Assert.Equal(210, clear["b"].Y);
        Assert.Equal(620, clear["c"].Y);

        var blocked = WidgetMagneticResizeLayout.ArrangeInteractive(original, "a", proposed, original,
            "LeftTop", 10, 18, out _, [new(-400, 401, 1100, 8)]);
        Assert.Equal(original["b"], blocked["b"]);
        Assert.Equal(original["c"], blocked["c"]);
    }

    [Fact]
    public void BlockedGrowthLeavesTheWholeContactingColumnInPlace()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 300), ["d"] = new(310, 410, 300, 300),
            ["e"] = new(0, 720, 300, 300)
        };
        var result = Arrange(original, "a", new(0, 0, 300, 650));
        Assert.Equal(original, result);
    }

    [Fact]
    public void GrowthUsesTheEmptySpaceUpToTheDistantMember()
    {
        var original = new Dictionary<string, RectInt32>
        { ["a"] = new(0, 0, 300, 200), ["b"] = new(0, 700, 300, 200) };
        var initial = Arrange(original, "a", new(0, 0, 300, 500));
        Assert.Equal(500, initial["a"].Height);
        Assert.Equal(original["b"], initial["b"]);
        var blocked = Arrange(original, "a", new(0, 0, 300, 1200));
        Assert.Equal(690, blocked["a"].Height);
        Assert.Equal(original["b"], blocked["b"]);
    }

    [Theory]
    [InlineData(300, 799)]
    [InlineData(300, 860)]
    public void ResizeFillsTheGapInTheSavedFiveWidgetLayout(int width, int height)
    {
        var attachment = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2251, 0, 420, 600), ["middle"] = new(2679, 0, 549, 799),
            ["upper-right"] = new(3236, 0, 604, 576), ["lower-right"] = new(3236, 584, 580, 616),
            ["lower-middle"] = new(2808, 807, 420, 616)
        };
        var original = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2151, 118, 420, 600), ["middle"] = new(2579, 124, 337, 799),
            ["upper-right"] = new(3344, 118, 373, 576), ["lower-right"] = new(3344, 702, 373, 616),
            ["lower-middle"] = new(2916, 925, 420, 616)
        };
        var proposed = new RectInt32(2579, 124, width, height);
        var result = WidgetMagneticResizeLayout.ArrangeInteractive(original, "middle", proposed, attachment,
            "RightTop", 8, 18, out bool reattached);

        Assert.True(reattached);
        Assert.Equal(proposed, result["middle"]);
        Assert.Equal(original["left"], result["left"]);
        Assert.Equal(proposed.X + proposed.Width + 8, result["upper-right"].X);
        Assert.Equal(result["upper-right"].X, result["lower-right"].X);
        Assert.Equal(result["upper-right"].Y + 584, result["lower-right"].Y);
        Assert.Equal(result["lower-right"].X - 428, result["lower-middle"].X);
        Assert.Equal(result["lower-right"].Y + 223, result["lower-middle"].Y);

        var size = result.ToDictionary(pair => pair.Key, pair => new SizeInt32(pair.Value.Width, 78));
        var compact = WidgetMagneticCollapseLayout.RestoreCompact(result,
            new Dictionary<string, RectInt32>(), size, "RightTop", 8, 18);
        var origin = WidgetCoordinatedMoveCalculator.GetUnion(compact.Values);
        Assert.Equal(result, WidgetMagneticCollapseLayout.RestoreExpanded(result,
            new(origin.X, origin.Y), result, "RightTop", 8, 18));
    }

    [Fact]
    public void FillingAGapDoesNotMoveAMemberThroughAnOccupiedSpace()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["source"] = new(0, 0, 200, 400), ["blocker"] = new(420, 0, 200, 400),
            ["detached"] = new(700, 0, 200, 400)
        };
        var attachment = original.ToDictionary();
        attachment["detached"] = new(210, 0, 200, 400);
        var result = WidgetMagneticResizeLayout.ArrangeInteractive(original, "source", new(0, 0, 180, 400), attachment,
            "LeftTop", 10, 12, out bool reattached);
        Assert.False(reattached);
        Assert.Equal(original["blocker"], result["blocker"]);
        Assert.Equal(original["detached"], result["detached"]);
    }

    [Fact]
    public void ShorteningAnEdgeSlidesItsConnectedRowUpIntoTheFreeSpace()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["source"] = new(0, 0, 300, 600), ["first"] = new(310, 400, 250, 240),
            ["second"] = new(570, 400, 250, 240)
        };
        var result = WidgetMagneticResizeLayout.ArrangeInteractive(original, "source", new(0, 0, 300, 350),
            original, "LeftTop", 10, 18, out bool reattached);
        Assert.True(reattached);
        Assert.Equal(310, result["first"].X);
        Assert.Equal(332, result["first"].Y);
        Assert.Equal(570, result["second"].X);
        Assert.Equal(result["first"].Y, result["second"].Y);
    }

    [Fact]
    public void GrowingTheTopEdgeStopsAtTheOverlappingUpperColumn()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 300), ["d"] = new(310, 410, 300, 300)
        };
        var result = Arrange(original, "c", new(0, 200, 300, 510));
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(900)]
    public void RightAlignedWidthCrossingsKeepTheUpperWidgetFixed(int width)
    {
        var original = new Dictionary<string, RectInt32>
        { ["a"] = new(400, 100, 300, 52), ["b"] = new(100, 154, 600, 400) };
        var result = Arrange(original, "b", new(700 - width, 154, width, 400));
        Assert.Equal(original["a"], result["a"]);
        Assert.Equal(700, result["b"].X + result["b"].Width);
    }

    [Fact]
    public void BlockedWidthGrowthKeepsBothRowsInPlace()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 300), ["d"] = new(310, 410, 300, 300)
        };
        var result = Arrange(original, "a", new(0, 0, 500, 400));
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("z")]
    public void LegacyTransitionReflowStillRepairsCoincidentBounds(string sourceId)
    {
        var original = new Dictionary<string, RectInt32>
        { [sourceId] = new(0, 0, 300, 400), ["other"] = new(0, 0, 300, 400) };
        var result = WidgetMagneticResizeLayout.Arrange(original, sourceId, new(0, 0, 300, 500),
            original, "LeftTop", 10, 18);
        Assert.Equal(500, result["other"].Y);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    public void CornerResizeKeepsAllFourMembersSeparated(string sourceId)
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 400), ["d"] = new(310, 410, 300, 400)
        };
        foreach (int width in new[] { 200, 300, 500 })
        foreach (int height in new[] { 200, 400, 650 })
        {
            RectInt32 source = original[sourceId];
            int x = sourceId is "b" or "d" ? source.X + source.Width - width : source.X;
            int y = sourceId is "c" or "d" ? source.Y + source.Height - height : source.Y;
            var proposed = new RectInt32(x, y, width, height);
            var result = Arrange(original, sourceId, proposed);
            Assert.InRange(result[sourceId].Width, Math.Min(proposed.Width, source.Width), proposed.Width);
            Assert.InRange(result[sourceId].Height, Math.Min(proposed.Height, source.Height), proposed.Height);
            foreach (var first in result)
            foreach (var second in result.Where(p => string.CompareOrdinal(p.Key, first.Key) > 0))
            {
                RectInt32 a = first.Value;
                RectInt32 b = second.Value;
                Assert.True(a.X + a.Width <= b.X || b.X + b.Width <= a.X ||
                    a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y);
            }
        }
    }

    private static IReadOnlyDictionary<string, RectInt32> Arrange(
        Dictionary<string, RectInt32> original, string sourceId, RectInt32 proposed)
    {
        RectInt32 accepted = WidgetMagneticResizePolicy.ConstrainGrowth(original[sourceId], proposed,
            original.Where(pair => pair.Key != sourceId).Select(pair => pair.Value).ToArray(), 10);
        return WidgetMagneticResizeLayout.ArrangeInteractive(original, sourceId, accepted, original, "LeftTop", 10, 18, out _);
    }
}
