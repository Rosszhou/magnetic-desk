using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticDockTests
{
    [Theory]
    [InlineData(0, 100, MagneticDockEdge.Right, MagneticDockEdge.Left)]
    [InlineData(208, 100, MagneticDockEdge.Left, MagneticDockEdge.Right)]
    [InlineData(108, -8, MagneticDockEdge.Bottom, MagneticDockEdge.Top)]
    [InlineData(108, 208, MagneticDockEdge.Top, MagneticDockEdge.Bottom)]
    public void Contact_FindsFacingEdges(int x, int y, MagneticDockEdge sourceEdge, MagneticDockEdge targetEdge)
    {
        MagneticDockContact? contact = WidgetMagneticDockPolicy.FindContact(
            new RectInt32(x, y, 100, 100), new RectInt32(108, 100, 92, 100), 8, 20, 28);
        Assert.NotNull(contact);
        Assert.Equal(sourceEdge, contact.Value.SourceEdge);
        Assert.Equal(targetEdge, contact.Value.TargetEdge);
        Assert.Equal(0, contact.Value.Distance);
    }

    [Fact]
    public void Contact_CornerOnlyDoesNotArmCountdown()
    {
        Assert.Null(WidgetMagneticDockPolicy.FindContact(
            new RectInt32(0, 0, 100, 100), new RectInt32(108, 100, 100, 100), 8, 20, 28));
    }

    [Fact]
    public void Contact_DistantEdgesDoNotArmCountdown()
    {
        Assert.Null(WidgetMagneticDockPolicy.FindContact(
            new RectInt32(0, 0, 100, 100), new RectInt32(150, 0, 100, 100), 8, 20, 28));
    }

    [Fact]
    public void Contact_ThresholdIncludesBoundaryAndPreservesParallelPosition()
    {
        MagneticDockContact? contact = WidgetMagneticDockPolicy.FindContact(
            new RectInt32(-20, 40, 100, 100), new RectInt32(108, 0, 100, 100), 8, 20, 28);
        Assert.NotNull(contact);
        Assert.Equal(20, contact.Value.Distance);
        Assert.Equal(0, contact.Value.DockedBounds.X);
        Assert.Equal(40, contact.Value.DockedBounds.Y);
        Assert.Equal(70, contact.Value.ContactCoordinate);
    }

    [Fact]
    public void Contact_DegenerateWindowIsIgnored()
    {
        Assert.Null(WidgetMagneticDockPolicy.FindContact(
            new RectInt32(0, 0, 0, 100), new RectInt32(8, 0, 100, 100), 8, 20, 28));
    }

    [Fact]
    public void Countdown_CompletesAfterThreeContinuousSeconds()
    {
        var countdown = new MagneticDockCountdown();
        countdown.Update("first/right", TimeSpan.FromSeconds(10));
        Assert.Equal(3, countdown.SecondsRemaining);
        countdown.Update("first/right", TimeSpan.FromSeconds(11));
        Assert.Equal(2, countdown.SecondsRemaining);
        countdown.Update("first/right", TimeSpan.FromSeconds(12));
        Assert.Equal(1, countdown.SecondsRemaining);
        countdown.Update("first/right", TimeSpan.FromSeconds(12.999));
        Assert.False(countdown.IsComplete);
        countdown.Update("first/right", TimeSpan.FromSeconds(13));
        Assert.True(countdown.IsComplete);
        Assert.Equal(0, countdown.SecondsRemaining);
    }

    [Fact]
    public void Countdown_LeavingAndReturningRequiresFullDuration()
    {
        var countdown = new MagneticDockCountdown();
        countdown.Update("first/right", TimeSpan.Zero);
        countdown.Update("first/right", TimeSpan.FromSeconds(2.9));
        countdown.Update(null, TimeSpan.FromSeconds(2.95));
        Assert.False(countdown.IsComplete);
        countdown.Update("first/right", TimeSpan.FromSeconds(3));
        Assert.Equal(3, countdown.SecondsRemaining);
        countdown.Update("first/right", TimeSpan.FromSeconds(5.9));
        Assert.False(countdown.IsComplete);
    }

    [Theory]
    [InlineData("second/right")]
    [InlineData("first/bottom")]
    public void Countdown_ChangingTargetOrEdgeRestarts(string newCandidate)
    {
        var countdown = new MagneticDockCountdown();
        countdown.Update("first/right", TimeSpan.Zero);
        countdown.Update("first/right", TimeSpan.FromSeconds(2.9));
        countdown.Update(newCandidate, TimeSpan.FromSeconds(3));
        Assert.Equal(0, countdown.Progress);
        Assert.Equal(3, countdown.SecondsRemaining);
        Assert.False(countdown.IsComplete);
    }

    [Fact]
    public void DragReleaseAndCaptureLoss_CancelPendingCountdown()
    {
        string source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.Interaction.cs"));
        string release = source[source.IndexOf("protected void EndWindowDragCore", StringComparison.Ordinal)..];
        string captureLoss = source[source.IndexOf("protected void DragPointerCaptureLostCore", StringComparison.Ordinal)..];
        Assert.Contains("CancelMagneticDockDrag(Config.Id)", release);
        Assert.Contains("CancelMagneticDockDrag(Config.Id)", captureLoss);
        Assert.Contains("if (_magneticDockCompleted)", source);
    }
}
