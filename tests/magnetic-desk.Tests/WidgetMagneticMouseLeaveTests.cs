using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticMouseLeaveTests
{
    [Fact]
    public void Collapse_RequiresFiveContinuousSecondsOutside()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        Assert.False(policy.Update(TimeSpan.Zero, false));
        Assert.False(policy.Update(TimeSpan.FromMilliseconds(4999), false));
        Assert.True(policy.Update(TimeSpan.FromSeconds(5), false));
    }

    [Fact]
    public void ReturningToAnyMember_RestartsTheFiveSecondWait()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        policy.Update(TimeSpan.Zero, false);
        Assert.False(policy.Update(TimeSpan.FromSeconds(4), true));
        Assert.False(policy.Update(TimeSpan.FromSeconds(8), false));
        Assert.False(policy.Update(TimeSpan.FromSeconds(12.999), false));
        Assert.True(policy.Update(TimeSpan.FromSeconds(13), false));
    }

    [Fact]
    public void ActiveMenuOrDrag_KeepsTheScopeOpen()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        Assert.False(policy.Update(TimeSpan.Zero, true));
        Assert.False(policy.Update(TimeSpan.FromMinutes(1), true));
        Assert.False(policy.Update(TimeSpan.FromMinutes(2), false));
        Assert.True(policy.Update(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5), false));
    }

    [Fact]
    public void ClockReset_StartsANewWait()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        policy.Update(TimeSpan.FromSeconds(20), false);
        Assert.False(policy.Update(TimeSpan.Zero, false));
        Assert.False(policy.Update(TimeSpan.FromSeconds(4), false));
    }

    [Fact]
    public void MagneticActivity_ResetsAnAlmostExpiredCollapseCountdown()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        policy.Update(TimeSpan.Zero, false);
        Assert.False(policy.Update(TimeSpan.FromMilliseconds(4900), true));
        Assert.False(policy.IsCounting);
        Assert.False(policy.Update(TimeSpan.FromSeconds(8), true));
        Assert.False(policy.Update(TimeSpan.FromSeconds(9), false));
        Assert.False(policy.Update(TimeSpan.FromMilliseconds(13999), false));
        Assert.True(policy.Update(TimeSpan.FromSeconds(14), false));
    }

    [Fact]
    public void ControlDrag_DetachesAtTheMovementThreshold()
    {
        var source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.Interaction.cs"));
        int distanceCheck = source.IndexOf("if (dragDistanceSquared < 16)", StringComparison.Ordinal);
        int moved = source.IndexOf("HasMovedTitleBarDrag = true", distanceCheck, StringComparison.Ordinal);
        int detach = source.IndexOf("DetachMagneticMemberForControlDrag(_titleBarDragControlPressed)", moved, StringComparison.Ordinal);
        Assert.True(distanceCheck < moved && moved < detach);
        Assert.Contains("!controlPressed || !IsDragging || !HasMovedTitleBarDrag", source);
        Assert.Contains("else if (!_detachedMagneticGroupThisDrag)", source);
    }

    [Fact]
    public void TimedExpansion_SuppressesTheEarlierSmartCollapseTimer()
    {
        var source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.Collapse.cs"));
        int schedule = source.IndexOf("private void ScheduleSmartCollapse", StringComparison.Ordinal);
        Assert.Contains("HasMouseLeaveAutoCollapse(this)", source[schedule..]);
        Assert.Contains("ArmMouseLeaveAutoCollapse(this)", source);
    }
}
