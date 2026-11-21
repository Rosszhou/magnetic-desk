using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticResizePolicyTests
{
    [Theory]
    [InlineData(600)]
    [InlineData(300)]
    [InlineData(200)]
    public void LowerPanelBlocksAtItsEdgeRegardlessOfWidth(int lowerWidth)
    {
        RectInt32 original = new(0, 0, 300, 400);
        var result = WidgetMagneticResizePolicy.ConstrainGrowth(original,
            new(0, 0, 300, 900), [new(0, 490, lowerWidth, 300)], 10);
        Assert.Equal(new RectInt32(0, 0, 300, 480), result);
    }

    [Fact]
    public void WiderPanelInAnotherColumnDoesNotBlockHeightGrowth()
    {
        RectInt32 proposed = new(0, 0, 300, 700);
        Assert.Equal(proposed, WidgetMagneticResizePolicy.ConstrainGrowth(new(0, 0, 300, 400),
            proposed, [new(300, 410, 600, 400)], 10));
    }

    [Fact]
    public void LeftAndTopDragKeepOppositeEdgesFixed()
    {
        var result = WidgetMagneticResizePolicy.ConstrainGrowth(new(100, 100, 300, 300), new(-100, -100, 500, 500),
            [new(0, 150, 30, 100), new(150, 0, 100, 50)], 8);
        Assert.Equal(new RectInt32(38, 58, 362, 342), result);
    }

    [Fact]
    public void BottomRightDragCanGrowRightWhenBlockedBelow()
    {
        var result = WidgetMagneticResizePolicy.ConstrainGrowth(new(0, 0, 300, 400), new(0, 0, 550, 800),
            [new(0, 490, 300, 100)], 10);
        Assert.Equal(new RectInt32(0, 0, 550, 480), result);
    }

    [Fact]
    public void CornerNarrowingCanFreeThePathForGrowth()
    {
        RectInt32 proposed = new(0, 0, 210, 700);
        Assert.Equal(proposed, WidgetMagneticResizePolicy.ConstrainGrowth(new(0, 0, 300, 400), proposed,
            [new(235, 490, 100, 110)], 8));
    }

    [Fact]
    public void AnExistingTighterGapDoesNotPushEitherPanelBackwards()
    {
        RectInt32 original = new(0, 0, 300, 400);
        Assert.Equal(original, WidgetMagneticResizePolicy.ConstrainGrowth(original, new(0, 0, 350, 400),
            [new(303, 0, 100, 400)], 8));
    }
}
