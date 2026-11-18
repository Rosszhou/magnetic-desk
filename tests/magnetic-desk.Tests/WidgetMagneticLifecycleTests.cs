using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MixedTransitionKeepsTheSavedVerticalOffset(bool leftCompact)
    {
        var expanded = new Dictionary<string, RectInt32>
        { ["left"] = new(100, 323, 420, 616), ["right"] = new(528, 100, 580, 616) };
        var sizes = expanded.ToDictionary(pair => pair.Key, pair => new SizeInt32(pair.Value.Width,
            (pair.Key == "left") == leftCompact ? 78 : 616));
        var actual = WidgetMagneticCollapseLayout.ArrangeTransition(expanded, sizes, "RightTop", 8, 12);
        Assert.Equal(223, actual["left"].Y - actual["right"].Y);
        Assert.Equal(323, expanded["left"].Y);
        Assert.Equal(100, expanded["right"].Y);
    }

    [Fact]
    public void MixedTransitionMovesAContactingLowerMemberWithoutChangingSavedBounds()
    {
        var expanded = new Dictionary<string, RectInt32>
        { ["top"] = new(100, 100, 500, 600), ["bottom"] = new(100, 708, 500, 400) };
        var sizes = new Dictionary<string, SizeInt32> { ["top"] = new(500, 78), ["bottom"] = new(500, 400) };
        var actual = WidgetMagneticCollapseLayout.ArrangeTransition(expanded, sizes, "LeftTop", 8, 12);
        Assert.Equal(186, actual["bottom"].Y);
        Assert.Equal(708, expanded["bottom"].Y);
    }

    [Fact]
    public void UndoStatePreservesExplicitIndependentCollapse()
    {
        var config = new WidgetConfig { MagneticGroupId = "group", MagneticCollapseLinked = false };
        config.Metadata[WidgetManager.MagneticIndependentCollapseKey] = "true";
        var saved = WidgetLayoutState.Capture(config);
        config.Metadata.Remove(WidgetManager.MagneticIndependentCollapseKey);
        config.MagneticCollapseLinked = true;
        saved.Restore(config);
        Assert.False(config.MagneticCollapseLinked);
        Assert.Equal("true", config.Metadata[WidgetManager.MagneticIndependentCollapseKey]);
    }
}
