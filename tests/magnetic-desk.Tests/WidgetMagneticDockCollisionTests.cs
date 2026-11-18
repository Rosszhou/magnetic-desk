using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticDockCollisionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IncomingMemberSlidesPastTheOtherGroupMembersOnEveryEdge(int turns)
    {
        RectInt32 source = Rotate(new(193, 555, 418, 614), turns);
        RectInt32 target = Rotate(new(621, 555, 580, 614), turns);
        RectInt32[] obstacles = [Rotate(new(40, 0, 547, 768), turns), Rotate(new(596, 0, 604, 547), turns), target];
        var original = obstacles.ToArray();
        var contact = WidgetMagneticDockPolicy.FindContact(source, target, 10, 30, 42, obstacles);
        Assert.NotNull(contact);
        Assert.Equal(Rotate(new(193, 778, 418, 614), turns), contact.Value.DockedBounds);
        AssertClear(contact.Value.DockedBounds, obstacles, 10);
        Assert.Equal(original, obstacles);
    }

    [Theory]
    [InlineData(-3000, -1200)]
    [InlineData(0, 0)]
    [InlineData(4000, 2000)]
    public void CollisionPlacementIsIndependentOfMonitorCoordinates(int dx, int dy)
    {
        RectInt32 source = new(193 + dx, 555 + dy, 418, 614);
        RectInt32 target = new(621 + dx, 555 + dy, 580, 614);
        RectInt32[] obstacles = [new(40 + dx, dy, 547, 768), target];
        var contact = WidgetMagneticDockPolicy.FindContact(source, target, 10, 30, 42, obstacles);
        Assert.NotNull(contact);
        Assert.Equal(778 + dy, contact.Value.DockedBounds.Y);
        Assert.Equal(source.X, contact.Value.DockedBounds.X);
    }

    [Fact]
    public void CompletelyBlockedEdgeDoesNotProduceADockingContact()
    {
        RectInt32 source = new(193, 555, 418, 614);
        RectInt32 target = new(621, 555, 580, 614);
        Assert.Null(WidgetMagneticDockPolicy.FindContact(source, target, 10, 30, 42,
            [new(40, -100, 547, 2000), target]));
    }

    [Fact]
    public void AFreeIntervalBetweenTwoBlockersCanBeUsed()
    {
        RectInt32 source = new(0, 90, 100, 40);
        RectInt32 target = new(110, 0, 100, 300);
        RectInt32[] obstacles = [new(0, -200, 100, 300), new(0, 170, 100, 400), target];
        var contact = WidgetMagneticDockPolicy.FindContact(source, target, 10, 20, 28, obstacles);
        Assert.NotNull(contact);
        Assert.Equal(110, contact.Value.DockedBounds.Y);
        AssertClear(contact.Value.DockedBounds, obstacles, 10);
    }

    [Fact]
    public void EveryIncomingGroupMemberParticipatesInCollisionChecking()
    {
        RectInt32 source = new(0, 0, 100, 100);
        RectInt32 companion = new(0, -110, 100, 100);
        RectInt32 target = new(110, 0, 100, 300);
        RectInt32[] obstacles = [new(0, -110, 100, 100), target];
        var contact = WidgetMagneticDockPolicy.FindContact(source, target, 10, 20, 28, obstacles, [source, companion]);
        Assert.NotNull(contact);
        Assert.Equal(110, contact.Value.DockedBounds.Y);
        AssertClear(contact.Value.DockedBounds, obstacles, 10);
        AssertClear(new(0, 0, 100, 100), obstacles, 10);
    }

    [Fact]
    public void MenuJoinChoosesAFreePositionForTheWholeIncomingGroup()
    {
        RectInt32 source = new(0, 0, 100, 100);
        RectInt32 companion = new(0, -110, 100, 100);
        RectInt32 target = new(110, 0, 100, 300);
        RectInt32[] obstacles = [new(0, -110, 100, 100), target];
        var contact = WidgetMagneticDockPlacement.FindBeside(source, target, obstacles, [source, companion], 10, 28);
        Assert.NotNull(contact);
        Assert.Equal(new RectInt32(0, 110, 100, 100), contact.Value.DockedBounds);
    }

    [Fact]
    public void CompletionRevalidationDetectsANewBlocker()
    {
        RectInt32 source = new(193, 555, 418, 614);
        RectInt32 target = new(621, 555, 580, 614);
        var preview = WidgetMagneticDockPolicy.FindContact(source, target, 10, 30, 42)!.Value;
        var revised = WidgetMagneticDockPlacement.Resolve(preview, source, target,
            [new(40, 0, 547, 768), target], [source], 10, 42);
        Assert.NotNull(revised);
        Assert.NotEqual(preview.DockedBounds, revised.Value.DockedBounds);
        Assert.Equal(778, revised.Value.DockedBounds.Y);
    }

    [Fact]
    public void CompletionRevalidationDetectsATargetThatMovedAway()
    {
        RectInt32 source = new(0, 0, 100, 100);
        RectInt32 target = new(110, 0, 100, 100);
        var preview = WidgetMagneticDockPolicy.FindContact(source, target, 10, 20, 28)!.Value;
        RectInt32 moved = new(210, 0, 100, 100);
        var revised = WidgetMagneticDockPlacement.Resolve(preview, source, moved, [moved], [source], 10, 28);
        Assert.NotNull(revised);
        Assert.NotEqual(preview.DockedBounds, revised.Value.DockedBounds);
        Assert.Equal(100, revised.Value.DockedBounds.X);
    }

    [Fact]
    public void ExpandedFootprintOfACompactNeighborIsReserved()
    {
        RectInt32 source = new(193, 555, 418, 614);
        RectInt32 target = new(621, 555, 580, 614);
        var contact = WidgetMagneticDockPolicy.FindContact(source, target, 10, 30, 42,
            [new(40, 0, 547, 78), new(40, 0, 547, 768), target]);
        Assert.NotNull(contact);
        Assert.Equal(778, contact.Value.DockedBounds.Y);
    }

    [Fact]
    public void TargetAlwaysParticipatesWhenCheckingAnIncomingCompanion()
    {
        RectInt32 source = new(0, 0, 100, 100);
        RectInt32 target = new(110, 0, 100, 300);
        Assert.Null(WidgetMagneticDockPolicy.FindContact(source, target, 10, 20, 28,
            [], [source, new(110, 0, 100, 100)]));
    }

    [Fact]
    public void ZeroAreaLegacyBoundsDoNotBlockAFreePosition()
    {
        RectInt32 source = new(0, 0, 100, 100);
        RectInt32 target = new(110, 0, 100, 100);
        var contact = WidgetMagneticDockPolicy.FindContact(source, target, 10, 20, 28, [new(0, -10, 0, 200)]);
        Assert.NotNull(contact);
        Assert.Equal(source, contact.Value.DockedBounds);
    }

    private static RectInt32 Rotate(RectInt32 bounds, int turns)
    {
        for (int i = 0; i < turns; i++) bounds = new(-bounds.Y - bounds.Height, bounds.X, bounds.Height, bounds.Width);
        return bounds;
    }

    private static void AssertClear(RectInt32 source, IEnumerable<RectInt32> obstacles, int spacing)
    {
        foreach (RectInt32 obstacle in obstacles)
            Assert.True(source.X + source.Width + spacing <= obstacle.X || obstacle.X + obstacle.Width + spacing <= source.X ||
                source.Y + source.Height + spacing <= obstacle.Y || obstacle.Y + obstacle.Height + spacing <= source.Y);
    }
}
