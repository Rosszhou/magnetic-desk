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
        Assert.Contains("else if (!IsCompactBoundsStateActive && !_detachedMagneticGroupThisDrag)", source);
    }

    [Fact]
    public void TimedExpansion_SuppressesTheEarlierSmartCollapseTimer()
    {
        var source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.Collapse.cs"));
        int schedule = source.IndexOf("private void ScheduleSmartCollapse", StringComparison.Ordinal);
        Assert.Contains("HasMouseLeaveAutoCollapse(this)", source[schedule..]);
        Assert.Contains("ArmMouseLeaveAutoCollapse(this)", source);
    }

    [Fact]
    public void PreArmLeadWindow_ActivatesOnlyJustBeforeFiveSecondExpiry()
    {
        var policy = new WidgetMouseLeaveCollapsePolicy();
        Assert.False(policy.ShouldPreArmCollapse(TimeSpan.Zero));
        Assert.False(policy.Update(TimeSpan.Zero, false));
        Assert.False(policy.ShouldPreArmCollapse(TimeSpan.FromMilliseconds(4499)));
        Assert.True(policy.ShouldPreArmCollapse(TimeSpan.FromMilliseconds(4500)));
        Assert.True(policy.ShouldPreArmCollapse(TimeSpan.FromMilliseconds(4999)));
        Assert.False(policy.Update(TimeSpan.FromMilliseconds(4999), true));
        Assert.False(policy.ShouldPreArmCollapse(TimeSpan.FromMilliseconds(4999)));
    }

    [Fact]
    public void MouseLeaveCountdown_BlocksQuiescenceTrimAndPreArmsFrameClock()
    {
        var manager = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Services/WidgetManager.cs"));
        var mouseLeave = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Services/WidgetManager.MouseLeaveCollapse.cs"));
        int visualWork = manager.IndexOf("internal bool HasActiveVisualWork =>", StringComparison.Ordinal);
        int ambientWork = manager.IndexOf("internal bool HasAmbientVisualWork =>", visualWork, StringComparison.Ordinal);
        Assert.Contains("HasActiveMouseLeaveCountdown", manager[visualWork..ambientWork]);
        Assert.Contains("ShouldPreArmCollapse(now)", mouseLeave);
        Assert.Contains("PreArmCompactAnimationClock()", mouseLeave);
        Assert.Contains("HideMouseLeaveCountdownForCollapse()", mouseLeave);
    }

    [Fact]
    public void CollapseSettle_UsesTwoPhasePerVsyncHostShrinkAndDynamicRefreshBudget()
    {
        var compositionBounds = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.CompositionBounds.cs"));
        var windowClip = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Platform/Win32Helper.WindowClip.cs"));
        var coordinator = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Services/WidgetCompactAnimationCoordinator.cs"));
        var shell = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Controls/WidgetShell.xaml.cs"));

        Assert.Contains("TryBeginTwoPhaseCollapseSettle", compositionBounds);
        Assert.Contains("followsCollapse", compositionBounds);
        Assert.Contains("SetRoundedAnimationWindowClip", compositionBounds);
        Assert.Contains("ScheduleDeferredCollapseClipClear", compositionBounds);
        Assert.Contains("keepDestinationSizeLocked: true", compositionBounds);
        Assert.Contains("EnqueueDeferredBoundsSettle", compositionBounds);
        Assert.Contains("ReleaseLockedDestinationSize(", compositionBounds);
        Assert.Contains("sizeOnly:", compositionBounds);
        Assert.Contains("DisposeCompositionBoundsBackground", compositionBounds);
        Assert.Contains("Math.Clamp(effectiveBudget, 4, 17)", compositionBounds);

        Assert.Contains("CreateRoundRectRgn", windowClip);
        Assert.Contains("SetRoundedAnimationWindowClip", windowClip);

        Assert.Contains("DeferredBoundsSettles", coordinator);
        Assert.Contains("EnqueueDeferredBoundsSettle", coordinator);
        Assert.Contains("DeferredBoundsSettles.Count > 0", coordinator);

        int completeIndex = shell.IndexOf("public void CompleteCompactTransition", StringComparison.Ordinal);
        int cancelIndex = shell.IndexOf("public void CancelCompactTransition", completeIndex, StringComparison.Ordinal);
        string completeBody = shell[completeIndex..cancelIndex];
        Assert.True(
            completeBody.IndexOf("SetCollapsed(", StringComparison.Ordinal) <
            completeBody.IndexOf("SetBackgroundCornerRadius(", StringComparison.Ordinal));
    }
}
