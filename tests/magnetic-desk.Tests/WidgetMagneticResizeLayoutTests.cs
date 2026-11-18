using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticResizeLayoutTests
{
    [Theory]
    [InlineData(783, 793)]
    [InlineData(400, 535)]
    public void ResizingUpperLeftMovesOnlyItsLowerSupport(int height, int lowerY)
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
    public void HeightGrowthPropagatesThroughTheContactingColumn()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 300), ["d"] = new(310, 410, 300, 300),
            ["e"] = new(0, 720, 300, 300)
        };
        var result = Arrange(original, "a", new(0, 0, 300, 650));
        Assert.Equal(660, result["c"].Y);
        Assert.Equal(970, result["e"].Y);
        Assert.Equal(original["b"], result["b"]);
        Assert.Equal(original["d"], result["d"]);
    }

    [Fact]
    public void DistantMembersMoveOnlyWhenTheGrowingEdgeReachesThem()
    {
        var original = new Dictionary<string, RectInt32>
        { ["a"] = new(0, 0, 300, 200), ["b"] = new(0, 700, 300, 200) };
        Assert.Equal(700, Arrange(original, "a", new(0, 0, 300, 500))["b"].Y);
        Assert.Equal(810, Arrange(original, "a", new(0, 0, 300, 800))["b"].Y);
    }

    [Fact]
    public void GrowingTheTopEdgePushesOnlyTheOverlappingUpperColumn()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 300), ["d"] = new(310, 410, 300, 300)
        };
        var result = Arrange(original, "c", new(0, 200, 300, 510));
        Assert.Equal(-210, result["a"].Y);
        Assert.Equal(original["b"], result["b"]);
        Assert.Equal(original["d"], result["d"]);
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
    public void WidthGrowthDoesNotMoveTheOtherRow()
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
            ["c"] = new(0, 410, 300, 300), ["d"] = new(310, 410, 300, 300)
        };
        var result = Arrange(original, "a", new(0, 0, 500, 400));
        Assert.Equal(510, result["b"].X);
        Assert.Equal(original["d"], result["d"]);
        Assert.Equal(original["c"], result["c"]);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("z")]
    public void ResizingRepairsLegacyCoincidentBounds(string sourceId)
    {
        var original = new Dictionary<string, RectInt32>
        { [sourceId] = new(0, 0, 300, 400), ["other"] = new(0, 0, 300, 400) };
        var result = Arrange(original, sourceId, new(0, 0, 300, 500));
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
            Assert.Equal(proposed, result[sourceId]);
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
        Dictionary<string, RectInt32> original, string sourceId, RectInt32 proposed) =>
        WidgetMagneticResizeLayout.Arrange(original, sourceId, proposed, original, "LeftTop", 10, 18);
}
