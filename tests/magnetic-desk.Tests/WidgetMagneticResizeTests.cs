using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticResizeTests
{
    [Theory]
    [InlineData(200, 410)]
    [InlineData(500, 410)]
    [InlineData(600, 710)]
    public void GrowingLeftPanel_OnlyPushesLowerPanelWhenTheirWidthsOverlap(int lowerWidth, int expectedY)
    {
        var reference = new Dictionary<string, RectInt32>
        {
            ["left"] = new(0, 0, 300, 400),
            ["right"] = new(310, 0, 500, 400),
            ["lower"] = new(810 - lowerWidth, 410, lowerWidth, 300)
        };
        var sizes = reference.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        sizes["left"] = new(300, 700);
        var attachment = reference.ToDictionary();
        attachment["lower"] = new(210, 410, 600, 300);
        var actual = WidgetMagneticLayout.Capture(reference, resizingId: "left")
            .WithAlignment(attachment).WithSpacing(reference).Arrange(sizes, 0, 0);
        Assert.Equal(expectedY, actual["lower"].Y);
        Assert.Equal(0, actual["right"].Y);
        AssertNoOverlaps(actual.Values);
    }

    [Fact]
    public void ThinMembersOnTheSameRow_AreKeptSideBySide()
    {
        var reference = new Dictionary<string, RectInt32>
        { ["left"] = new(0, 0, 40, 1), ["right"] = new(42, 0, 40, 1) };
        var sizes = new Dictionary<string, SizeInt32>
        { ["left"] = new(100, 78), ["right"] = new(40, 78) };
        var actual = WidgetMagneticLayout.Capture(reference).Arrange(sizes, 0, 0, 2);
        Assert.Equal(102, actual["right"].X);
        Assert.Equal(0, actual["right"].Y);
        AssertNoOverlaps(actual.Values);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(604)]
    [InlineData(1240)]
    public void ResizingLowerCapsule_KeepsNearlyAlignedUpperRowTogether(int lowerWidth)
    {
        var reference = new Dictionary<string, RectInt32>
        {
            ["left"] = new(2175, 448, 420, 576),
            ["right"] = new(2603, 448, 604, 567),
            ["lower"] = new(2603, 1023, 604, 616)
        };
        var sizes = new Dictionary<string, SizeInt32>
        {
            ["left"] = new(420, 78),
            ["right"] = new(604, 78),
            ["lower"] = new(lowerWidth, 78)
        };
        var actual = WidgetMagneticLayout.Capture(reference).Arrange(sizes, 100, 100, 2);
        Assert.Equal(actual["left"].X + 422, actual["right"].X);
        Assert.Equal(actual["left"].Y, actual["right"].Y);
        Assert.Equal(actual["right"].Y + 80, actual["lower"].Y);
        Assert.Equal(actual["right"].X + 604, actual["lower"].X + lowerWidth);
        AssertNoOverlaps(actual.Values);
    }

    [Theory]
    [InlineData(250, 500)]
    [InlineData(700, 500)]
    [InlineData(400, 300)]
    [InlineData(400, 900)]
    public void ResizingASpanningPanel_ReflowsTheRightColumn(int width, int height)
    {
        var original = new Dictionary<string, RectInt32>
        {
            ["a"] = new(100, 100, 400, 500), ["b"] = new(510, 100, 300, 240), ["c"] = new(510, 350, 300, 250)
        };
        var layout = WidgetMagneticLayout.Capture(original);
        var sizes = original.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        sizes["a"] = new(width, height);
        var actual = layout.Arrange(sizes, 100, 100);
        Assert.Equal(100 + width + 10, actual["b"].X);
        Assert.Equal(actual["b"].X, actual["c"].X);
        Assert.Equal(350, actual["c"].Y);
        AssertNoOverlaps(actual.Values);
        var compact = WidgetMagneticCollapseLayout.Arrange(actual.Select(p => new MagneticCompactMember(
            p.Key, p.Value, new SizeInt32(p.Key == "a" ? width : 300, 48))).ToArray());
        Assert.Equal(100 + width + 2, compact["b"].X);
        Assert.Equal(compact["b"].X, compact["c"].X);
        Assert.Equal(compact["b"].Y + 50, compact["c"].Y);
        Assert.Equal(width, compact["a"].Width);
        AssertNoOverlaps(compact.Values);
    }

    [Fact]
    public void ResizeInTheFirstRow_PushesTheEntireLowerRow()
    {
        var bounds = new Dictionary<string, RectInt32>
        { ["a"] = new(0, 0, 300, 400), ["b"] = new(310, 0, 300, 400),
          ["c"] = new(0, 410, 300, 400), ["d"] = new(310, 410, 300, 400) };
        var sizes = bounds.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        sizes["a"] = new(550, 650);
        var actual = WidgetMagneticLayout.Capture(bounds).Arrange(sizes, 0, 0);
        Assert.Equal(560, actual["b"].X);
        Assert.Equal(660, actual["c"].Y);
        Assert.Equal(660, actual["d"].Y);
        AssertNoOverlaps(actual.Values);
    }

    [Fact]
    public void LegacyOverlappingBounds_AreRepairedDeterministically()
    {
        var bounds = new Dictionary<string, RectInt32>
        { ["a"] = new(0, 0, 400, 500), ["b"] = new(50, 20, 400, 500), ["c"] = new(50, 20, 300, 500) };
        var sizes = bounds.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        var actual = WidgetMagneticLayout.Capture(bounds).Arrange(sizes, 0, 0);
        Assert.Equal(3, actual.Count);
        AssertNoOverlaps(actual.Values);
    }

    [Fact]
    public void SharedCountdown_ExposesTheSameProgressAndResetsOnActivity()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        policy.Update(TimeSpan.Zero, false);
        Assert.True(policy.IsCounting);
        Assert.Equal(.6, policy.RemainingFraction(TimeSpan.FromSeconds(2)), 6);
        policy.Update(TimeSpan.FromSeconds(2), true);
        Assert.False(policy.IsCounting);
        Assert.Equal(1, policy.RemainingFraction(TimeSpan.FromSeconds(3)));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(600)]
    [InlineData(900)]
    public void RightAlignedAttachment_SurvivesWidthCrossings(int lowerWidth)
    {
        var reference = new Dictionary<string, RectInt32>
        { ["top"] = new(400, 100, 300, 400), ["bottom"] = new(100, 508, 600, 400) };
        var sizes = new Dictionary<string, SizeInt32>
        { ["top"] = new(300, 52), ["bottom"] = new(lowerWidth, 400) };
        var actual = WidgetMagneticLayout.Capture(reference).Arrange(sizes, 100, 100, 2);
        Assert.Equal(actual["top"].X + actual["top"].Width, actual["bottom"].X + actual["bottom"].Width);
        Assert.Equal(actual["top"].Y + 54, actual["bottom"].Y);
        AssertNoOverlaps(actual.Values);
        sizes["top"] = new(300, 400);
        var expanded = WidgetMagneticLayout.Capture(reference).Arrange(sizes, 100, 100, 8);
        Assert.Equal(expanded["top"].X + 300, expanded["bottom"].X + lowerWidth);
    }

    [Fact]
    public void BottomAlignedAttachment_SurvivesHeightCrossings()
    {
        var reference = new Dictionary<string, RectInt32>
        { ["left"] = new(0, 300, 300, 200), ["right"] = new(308, 0, 300, 500) };
        var sizes = new Dictionary<string, SizeInt32>
        { ["left"] = new(300, 200), ["right"] = new(300, 100) };
        var actual = WidgetMagneticLayout.Capture(reference).Arrange(sizes, 0, 0, 8);
        Assert.Equal(actual["left"].Y + 200, actual["right"].Y + 100);
    }

    [Fact]
    public void CompactionPreservesAMovedGroupsOrigin()
    {
        var reference = new Dictionary<string, RectInt32>
        { ["a"] = new(100, 100, 300, 400), ["b"] = new(410, 100, 300, 400) };
        var moved = reference.ToDictionary(p => p.Key,
            p => new RectInt32(p.Value.X + 120, p.Value.Y + 80, p.Value.Width, p.Value.Height));
        var sizes = moved.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, 52));
        var compact = WidgetMagneticCollapseLayout.RestoreCompact(moved,
            new Dictionary<string, RectInt32>(), sizes);
        Assert.Equal(220, compact["a"].X);
        Assert.Equal(180, compact["a"].Y);
        Assert.Equal(522, compact["b"].X);
    }

    [Fact]
    public void ReferenceAlignment_UsesTheCurrentVisibleSpacing()
    {
        var reference = new Dictionary<string, RectInt32>
        { ["a"] = new(400, 100, 300, 400), ["b"] = new(100, 508, 600, 400) };
        var visible = new Dictionary<string, RectInt32>
        { ["a"] = new(400, 100, 300, 52), ["b"] = new(100, 154, 600, 400) };
        var sizes = visible.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        sizes["b"] = new(200, 400);
        var actual = WidgetMagneticLayout.Capture(reference).WithSpacing(visible).Arrange(sizes, 100, 100);
        Assert.Equal(154, actual["b"].Y);
        Assert.Equal(actual["a"].X + 300, actual["b"].X + 200);
    }

    [Theory]
    [InlineData("RightTop", 200, true)]
    [InlineData("RightTop", 800, true)]
    [InlineData("LeftTop", 200, false)]
    [InlineData("LeftTop", 800, false)]
    public void InitiallyEqualWidths_RetainTheChosenAttachmentAnchor(string anchor, int width, bool alignRight)
    {
        var reference = new Dictionary<string, RectInt32>
        { ["top"] = new(100, 100, 600, 400), ["bottom"] = new(100, 508, 600, 400) };
        var sizes = new Dictionary<string, SizeInt32>
        { ["top"] = new(600, 52), ["bottom"] = new(width, 400) };
        var actual = WidgetMagneticLayout.Capture(reference, preferredAnchor: anchor).Arrange(sizes, 100, 100, 2);
        Assert.Equal(actual["top"].X + (alignRight ? 600 : 0), actual["bottom"].X + (alignRight ? width : 0));
    }

    private static void AssertNoOverlaps(IEnumerable<RectInt32> rectangles)
    {
        var bounds = rectangles.ToArray();
        for (int i = 0; i < bounds.Length; i++)
            for (int j = i + 1; j < bounds.Length; j++)
                Assert.True(bounds[i].X + bounds[i].Width <= bounds[j].X || bounds[j].X + bounds[j].Width <= bounds[i].X ||
                    bounds[i].Y + bounds[i].Height <= bounds[j].Y || bounds[j].Y + bounds[j].Height <= bounds[i].Y);
    }
}
