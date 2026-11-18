using System.Diagnostics;
using MagneticDesk.Models;
using MagneticDesk.Views;
using MagneticDesk.Platform;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private readonly WidgetLayoutGuideOverlay _magneticDockOutline = new();
    private readonly Stopwatch _magneticDockClock = Stopwatch.StartNew();
    private readonly MagneticDockCountdown _magneticDockCountdown = new();
    private DispatcherQueueTimer? _magneticDockTimer;
    private IDesktopWidgetWindow? _magneticDockSource;
    internal string? ActiveMagneticAnimationStyle => _magneticDockSource is { } source
        ? source.Config.MagneticAnimationStyle ?? nameof(MagneticAnimationStyle.Classic)
        : null;
    private IDesktopWidgetWindow? _magneticDockTarget;
    private MagneticDockContact? _magneticDockContact;
    private TimeSpan? _magneticDockCompletedAt;

    public bool UpdateMagneticDockDrag(string sourceId)
    {
        IDesktopWidgetWindow? source = GetLoadedDesktopWindows().FirstOrDefault(w => w.Config.Id == sourceId);
        if (source is null || !source.IsTitleBarDragTransitionActive)
        {
            CancelMagneticDockDrag(sourceId);
            return false;
        }
        if (_magneticDockSource != source)
        {
            ClearMagneticDockFeedback();
            _magneticDockSource = source;
            ResetMouseLeaveCountdown(source);
            _magneticDockTimer ??= CreateMagneticDockTimer();
            _magneticDockTimer.Start();
        }
        RefreshMagneticDockFeedback();
        return _magneticDockTarget is not null || _magneticDockExpansion is not null;
    }

    public void CancelMagneticDockDrag(string sourceId)
    {
        if (_magneticDockSource?.Config.Id == sourceId && _magneticDockCompletedAt is null)
        {
            ClearMagneticDockFeedback();
        }
    }

    private DispatcherQueueTimer CreateMagneticDockTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(40);
        timer.IsRepeating = true;
        timer.Tick += (_, _) => RefreshMagneticDockFeedback();
        return timer;
    }

    private void RefreshMagneticDockFeedback()
    {
        if (_magneticDockSource is not { } source)
        {
            return;
        }
        IReadOnlyList<IDesktopWidgetWindow> windows = GetLoadedDesktopWindows();
        if (!windows.Contains(source) || !source.Visible || !source.CanParticipateInCoordinatedMove)
        {
            ClearMagneticDockFeedback();
            return;
        }
        if (_magneticDockCompletedAt is { } completedAt)
        {
            if (_magneticDockClock.Elapsed - completedAt >= TimeSpan.FromMilliseconds(750))
            {
                ClearMagneticDockFeedback();
            }
            else if (_magneticDockTarget is { } completedTarget && windows.Contains(completedTarget) && completedTarget.Visible)
            {
                ShowMagneticDockFeedback(source, completedTarget, completed: true);
            }
            else
            {
                ClearMagneticDockFeedback();
            }
            return;
        }
        if (!source.IsTitleBarDragTransitionActive || _coordinatedMoveSession is not null)
        {
            ClearMagneticDockFeedback();
            return;
        }

        double scale = Win32Helper.GetDpiScaleForWindow(source.WindowHandle, source.WindowContentRoot?.XamlRoot);
        int spacing = Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetShell.WidgetSnapSpacing * scale));
        int threshold = Math.Max(1, (int)Math.Round(20 * scale));
        int minimumOverlap = Math.Max(1, (int)Math.Round(28 * scale));
        if (PrepareMagneticDockTarget(source, windows, spacing, threshold, minimumOverlap))
        {
            _magneticDockOutline.Hide();
            _magneticDockCountdown.Update(null, _magneticDockClock.Elapsed);
            return;
        }
        RectInt32 sourceBounds = source is WidgetWindowBase sourceView
            ? sourceView.MagneticDockBounds : source.CoordinatedMoveBounds;
        var obstacles = windows.Where(window => window != source && window.Visible)
            .SelectMany(MagneticDockOccupancy).ToArray();
        IDesktopWidgetWindow? target = null;
        MagneticDockContact? best = null;
        foreach (IDesktopWidgetWindow candidate in windows)
        {
            if (candidate == source || !candidate.Visible || !candidate.CanParticipateInCoordinatedMove ||
                candidate.IsCompactArrangementActive || candidate.IsTitleBarDragTransitionActive ||
                IsMagneticLayoutLocked(candidate.Config) ||
                (source is WidgetWindowBase view && !view.CanMagneticallyDockTo(candidate.Config.Id)))
            {
                continue;
            }
            MagneticDockContact? contact = WidgetMagneticDockPolicy.FindContact(
                sourceBounds, candidate.CoordinatedMoveBounds, spacing, threshold, minimumOverlap, obstacles);
            contact ??= ExpandedDockApproach(source, candidate, sourceBounds, obstacles, spacing, threshold, minimumOverlap);
            if (contact is { } found && (best is null || found.Distance < best.Value.Distance))
            {
                target = candidate;
                best = found;
            }
        }
        if (target is null || best is null)
        {
            ResetMouseLeaveCountdown(_magneticDockTarget);
            HideMagneticDockBadges();
            _magneticDockOutline.Hide();
            if (_magneticDockExpansion is null) _magneticDockTarget = null;
            _magneticDockContact = null;
            _magneticDockCountdown.Update(null, _magneticDockClock.Elapsed);
            return;
        }

        if (_magneticDockExpansion is { } expansion && expansion.Target != target &&
            (expansion.GroupId is null || expansion.GroupId != GetMagneticGroupId(target.Config)))
        {
            HideMagneticDockBadges();
            _magneticDockOutline.Hide();
            RestoreMagneticDockExpansion();
            _magneticDockTarget = null;
            _magneticDockContact = null;
            _magneticDockCountdown.Update(null, _magneticDockClock.Elapsed);
            return;
        }

        if (_magneticDockTarget == target && _magneticDockContact is { } previous && previous.SourceEdge == best.Value.SourceEdge &&
            Math.Abs(previous.DockedBounds.X - best.Value.DockedBounds.X) + Math.Abs(previous.DockedBounds.Y - best.Value.DockedBounds.Y) <= Math.Round(6 * scale))
        {
            var stable = WidgetMagneticDockPlacement.Resolve(previous, sourceBounds, target.CoordinatedMoveBounds,
                obstacles, [sourceBounds], spacing, minimumOverlap);
            if (stable is { } valid && valid.DockedBounds.Equals(previous.DockedBounds)) best = valid;
        }
        bool changed = _magneticDockTarget != target || _magneticDockContact?.SourceEdge != best.Value.SourceEdge ||
            _magneticDockContact?.DockedBounds.Equals(best.Value.DockedBounds) != true;
        if (changed)
        {
            ResetMouseLeaveCountdown(_magneticDockTarget);
            ResetMouseLeaveCountdown(target);
            HideMagneticDockBadges();
            App.Log($"[MagneticDock] Countdown source={source.Config.Id} target={target.Config.Id} edge={best.Value.SourceEdge} seconds=3");
        }
        _magneticDockTarget = target;
        _magneticDockContact = best;
        RectInt32 destination = best.Value.DockedBounds;
        _magneticDockCountdown.Update($"{source.Config.Id}/{target.Config.Id}/{best.Value.SourceEdge}/{destination.X}/{destination.Y}/{destination.Width}/{destination.Height}", _magneticDockClock.Elapsed);
        _magneticDockOutline.Show(WidgetLayoutGuides.Outline(best.Value.DockedBounds, scale));
        ShowMagneticDockFeedback(source, target, completed: false);
        if (_magneticDockCountdown.IsComplete)
        {
            CompleteMagneticDock(source, target, best.Value);
        }
    }

    private void CompleteMagneticDock(IDesktopWidgetWindow source, IDesktopWidgetWindow target, MagneticDockContact contact)
    {
        CompleteCapsuleBarDrag(source.Config.Id, preserveActiveBounds: true);
        var windows = GetLoadedDesktopWindows();
        RectInt32 sourceBounds = source is WidgetWindowBase view ? view.MagneticDockBounds : source.CoordinatedMoveBounds;
        double scale = Win32Helper.GetDpiScaleForWindow(source.WindowHandle, source.WindowContentRoot?.XamlRoot);
        var validated = WidgetMagneticDockPlacement.Resolve(contact, sourceBounds,
            target.CoordinatedMoveBounds, windows.Where(window => window != source && window.Visible)
                .SelectMany(MagneticDockOccupancy).ToArray(), [sourceBounds],
            Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetShell.WidgetSnapSpacing * scale)),
            Math.Max(1, (int)Math.Round(28 * scale)));
        if (validated is null || !validated.Value.DockedBounds.Equals(contact.DockedBounds))
        {
            ClearMagneticDockFeedback();
            return;
        }
        if (IsMagneticLayoutLocked(source.Config) || IsMagneticLayoutLocked(target.Config) ||
            !source.TryCompleteMagneticDock(contact.DockedBounds))
        {
            ClearMagneticDockFeedback();
            return;
        }
        _magneticDockOutline.Hide();
        if (_layoutOperation is { } operation)
            _layoutOperation = operation with { Description = "Magnetic.Undo.Join" };
        _magneticDockExpansion = null;
        _committingMagneticDock = true;
        try { LinkMagneticGroups(source.Config, target.Config); }
        finally { _committingMagneticDock = false; }
        _settingsService.UpdateWidget(source.Config, notifySubscribers: false);
        _magneticDockCompletedAt = _magneticDockClock.Elapsed;
        CancelWidgetGroupDrag(source.Config.Id);
        NoteWidgetWindowsMoved();
        ShowMagneticDockFeedback(source, target, completed: true);
        App.Log($"[MagneticDock] Attached source={source.Config.Id} target={target.Config.Id} group={GetMagneticGroupId(source.Config)}");
    }

    private static RectInt32 MagneticDockPlanningBounds(IDesktopWidgetWindow window) =>
        window.IsCompactArrangementActive && window is WidgetWindowBase view
            ? view.Config.MagneticExpandedPlacement?.ToBounds() ?? view.MagneticExpandedBounds
            : window.CoordinatedMoveBounds;

    private static IEnumerable<RectInt32> MagneticDockOccupancy(IDesktopWidgetWindow window)
    {
        yield return window.CoordinatedMoveBounds;
        if (window.IsCompactArrangementActive) yield return MagneticDockPlanningBounds(window);
    }

    private void ShowMagneticDockFeedback(IDesktopWidgetWindow source, IDesktopWidgetWindow target, bool completed)
    {
        if (_magneticDockContact is not { } contact)
        {
            return;
        }
        ApplyMagneticDockBadge(source, contact.SourceEdge, contact.ContactCoordinate, completed);
        ApplyMagneticDockBadge(target, contact.TargetEdge, contact.ContactCoordinate, completed);
    }

    private void ApplyMagneticDockBadge(IDesktopWidgetWindow window, MagneticDockEdge edge, int coordinate, bool completed)
    {
        RectInt32 bounds = window.CoordinatedMoveBounds;
        double scale = Win32Helper.GetDpiScaleForWindow(window.WindowHandle, window.WindowContentRoot?.XamlRoot);
        bool vertical = edge is MagneticDockEdge.Left or MagneticDockEdge.Right;
        double offset = (coordinate - (vertical ? bounds.Y : bounds.X)) / scale;
        double animationTime = completed && _magneticDockCompletedAt is { } completedAt
            ? (_magneticDockClock.Elapsed - completedAt).TotalSeconds
            : _magneticDockClock.Elapsed.TotalSeconds;
        window.SetMagneticDockFeedback(edge, offset, _magneticDockCountdown.Progress, completed, animationTime);
    }

    private void HideMagneticDockBadges()
    {
        _magneticDockSource?.HideMagneticDockFeedback();
        _magneticDockTarget?.HideMagneticDockFeedback();
    }

    private void ClearMagneticDockFeedback()
    {
        _magneticDockTimer?.Stop();
        _magneticDockOutline.Hide();
        ResetMouseLeaveCountdown(_magneticDockSource);
        ResetMouseLeaveCountdown(_magneticDockTarget);
        HideMagneticDockBadges();
        RestoreMagneticDockExpansion();
        _magneticDockSource = null;
        _magneticDockTarget = null;
        _magneticDockContact = null;
        _magneticDockCompletedAt = null;
        _magneticDockCountdown.Update(null, _magneticDockClock.Elapsed);
    }
}
