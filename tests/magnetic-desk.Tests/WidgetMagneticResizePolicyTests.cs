using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticResizePolicyTests
{
    [Theory]
    [InlineData(600, 300, 700, true)]
    [InlineData(300, 300, 700, false)]
    [InlineData(200, 300, 700, false)]
    [InlineData(600, 600, 700, false)]
    [InlineData(600, 300, 350, false)]
    public void HeightGrowthDependsOnTheLowerWidth(int lowerWidth, int proposedWidth, int proposedHeight, bool rejected)
    {
        var bounds = new Dictionary<string, RectInt32>
        { ["upper"] = new(0, 0, 300, 400), ["lower"] = new(0, 410, lowerWidth, 400) };
        Assert.Equal(rejected, WidgetMagneticResizePolicy.RejectHeightGrowth("upper",
            new(0, 0, proposedWidth, proposedHeight), bounds));
    }

    [Fact]
    public void WiderPanelInAnotherColumnDoesNotBlockHeightGrowth()
    {
        var bounds = new Dictionary<string, RectInt32>
        { ["upper"] = new(0, 0, 300, 400), ["lower"] = new(310, 410, 600, 400) };
        Assert.False(WidgetMagneticResizePolicy.RejectHeightGrowth("upper", new(0, 0, 300, 700), bounds));
    }
}
