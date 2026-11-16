using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticPlacementTests
{
    [Theory]
    [InlineData("a", "a", true)]
    [InlineData("a", "b", false)]
    [InlineData("a", null, false)]
    [InlineData(null, "a", false)]
    [InlineData(null, null, false)]
    public void SharesMoveScope_RestrictsGroupedSources(string? source, string? candidate, bool expected)
    {
        Assert.Equal(expected, WidgetMagneticPlacement.SharesMoveScope(source, candidate));
    }

    [Theory]
    [InlineData(450, 210, 408, 200)]
    [InlineData(0, 210, 92, 200)]
    [InlineData(210, 450, 200, 408)]
    [InlineData(210, 0, 200, 112)]
    public void SnapBeside_ChoosesClosestEdge(int x, int y, int expectedX, int expectedY)
    {
        RectInt32 result = WidgetMagneticPlacement.SnapBeside(
            new RectInt32(x, y, 100, 80), new RectInt32(200, 200, 200, 200), 8);
        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(100, result.Width);
        Assert.Equal(80, result.Height);
    }

    [Fact]
    public void SnapBeside_NegativeSpacingBecomesZero()
    {
        RectInt32 result = WidgetMagneticPlacement.SnapBeside(
            new RectInt32(500, 200, 100, 80), new RectInt32(200, 200, 200, 200), -10);
        Assert.Equal(400, result.X);
    }
}
