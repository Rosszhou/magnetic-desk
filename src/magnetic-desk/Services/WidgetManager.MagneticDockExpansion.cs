using MagneticDesk.Models;
using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private sealed record DockExpansionMember(WidgetWindowBase Window, bool Collapsed, bool PersistedCollapsed);
    private sealed record DockExpansionSession(IDesktopWidgetWindow Target, string? GroupId, long Revision,
        RectInt32 CompactBounds, MagneticDockContact Approach, DockExpansionMember[] Members);
    private DockExpansionSession? _magneticDockExpansion;

    private bool PrepareMagneticDockTarget(IDesktopWidgetWindow source, IReadOnlyList<IDesktopWidgetWindow> windows,
        int spacing, int threshold, int overlap)
    {
        if (_magneticDockExpansion is { } active)
        {
            bool valid = windows.Contains(active.Target) && active.Target.Visible &&
                GetMagneticGroupId(active.Target.Config) == active.GroupId &&
                MagneticMembershipRevision(active.GroupId) == active.Revision;
            bool nearby = valid && (WidgetMagneticDockPolicy.FindContact(source.CoordinatedMoveBounds,
                active.CompactBounds, spacing, threshold * 2, overlap) is not null ||
                BoundsNear(source.CoordinatedMoveBounds, active.Target.CoordinatedMoveBounds, threshold));
            if (nearby) return active.Members.Any(member => member.Window.IsBoundsTransitionActive);
            HideMagneticDockBadges();
            _magneticDockOutline.Hide();
            _magneticDockContact = null;
            _magneticDockCountdown.Update(null, _magneticDockClock.Elapsed);
            RestoreMagneticDockExpansion();
            _magneticDockTarget = null;
        }
        IDesktopWidgetWindow? target = null;
        MagneticDockContact? closest = null;
        foreach (var candidate in windows)
        {
            if (candidate == source || !candidate.Visible || !candidate.IsCompactArrangementActive ||
                !candidate.CanParticipateInCoordinatedMove || candidate.IsTitleBarDragTransitionActive ||
                IsMagneticLayoutLocked(candidate.Config) ||
                source is WidgetWindowBase view && !view.CanMagneticallyDockTo(candidate.Config.Id)) continue;
            var contact = WidgetMagneticDockPolicy.FindContact(source.CoordinatedMoveBounds,
                candidate.CoordinatedMoveBounds, spacing, threshold, overlap);
            if (contact is null || closest is { } previous && previous.Distance <= contact.Value.Distance) continue;
            target = candidate;
            closest = contact;
        }
        if (target is not WidgetWindowBase targetView || closest is null) return false;
        NormalizeMagneticCollapseMembership(target.Config);
        string? groupId = GetMagneticGroupId(target.Config);
        var members = target.Config.MagneticCollapseLinked && groupId is not null
            ? MagneticCollapseWindows(target.Config) : [targetView];
        _magneticDockExpansion = new(target, groupId, MagneticMembershipRevision(groupId), target.CoordinatedMoveBounds,
            closest.Value, members.Select(window => new DockExpansionMember(window,
                window.IsCompactArrangementActive, window.Config.IsCollapsed)).ToArray());
        _magneticDockTarget = target;
        ResetMouseLeaveCountdown(target);
        target.RequestCompactState(false, animate: true);
        return true;
    }

    private MagneticDockContact? ExpandedDockApproach(IDesktopWidgetWindow source, IDesktopWidgetWindow target,
        RectInt32 sourceBounds, IReadOnlyList<RectInt32> obstacles, int spacing, int threshold, int overlap)
    {
        if (_magneticDockExpansion is not { } session || session.Target != target ||
            WidgetMagneticDockPolicy.FindContact(source.CoordinatedMoveBounds, session.CompactBounds,
                spacing, threshold * 2, overlap) is not { } approach ||
            approach.SourceEdge != session.Approach.SourceEdge) return null;
        return WidgetMagneticDockPlacement.Resolve(session.Approach with
        {
            DockedBounds = sourceBounds,
            Distance = approach.Distance
        }, sourceBounds, target.CoordinatedMoveBounds, obstacles, [sourceBounds], spacing, overlap);
    }

    private void RestoreMagneticDockExpansion()
    {
        if (_magneticDockExpansion is not { } session) return;
        _magneticDockExpansion = null;
        if (GetMagneticGroupId(session.Target.Config) != session.GroupId ||
            MagneticMembershipRevision(session.GroupId) != session.Revision) return;
        var members = session.Members.Where(member => member.Collapsed && member.Window.Visible &&
            member.Window.MagneticCollapseInitialized && !member.Window.MagneticSmartPinnedOpen &&
            member.Window.Config.IsCollapsed == member.PersistedCollapsed &&
            GetMagneticGroupId(member.Window.Config) == session.GroupId).Select(member => member.Window).ToArray();
        if (members.Length > 0) ApplyMagneticCollapse(members, true, false, true);
        foreach (var member in session.Members.Where(member => !member.Window.IsCompactArrangementActive))
            ArmMouseLeaveAutoCollapse(member.Window);
    }

    private static bool BoundsNear(RectInt32 a, RectInt32 b, int distance) =>
        a.X < b.X + b.Width + distance && b.X < a.X + a.Width + distance &&
        a.Y < b.Y + b.Height + distance && b.Y < a.Y + a.Height + distance;
}
