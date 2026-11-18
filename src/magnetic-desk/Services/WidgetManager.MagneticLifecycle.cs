using MagneticDesk.Models;
using MagneticDesk.Views;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal const string MagneticIndependentCollapseKey = "MagneticIndependentCollapse";
    private readonly Dictionary<string, long> _magneticMembershipRevisions = new(StringComparer.Ordinal);
    private bool _committingMagneticDock;

    private long MagneticMembershipRevision(string? id) => id is null ? 0 : _magneticMembershipRevisions.GetValueOrDefault(id);

    private void InvalidateMagneticMembership(string id, WidgetWindowBase? continuingDrag = null)
    {
        var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(window => GetMagneticGroupId(window.Config) == id).ToArray();
        if (!_committingMagneticDock &&
            (_magneticDockSource is { } source && GetMagneticGroupId(source.Config) == id ||
             _magneticDockTarget is { } target && GetMagneticGroupId(target.Config) == id))
        {
            var dragSource = _magneticDockSource as WidgetWindowBase;
            ClearMagneticDockFeedback();
            if (dragSource != continuingDrag)
            {
                if (dragSource?.InterruptMagneticDrag() == true) CompleteLayoutOperation(dragSource.Config.Id);
                _layoutGuides?.EndDrag();
            }
        }
        if (_coordinatedMoveSession is { } move && windows.Any(window =>
            WinRT.Interop.WindowNative.GetWindowHandle(window) == move.SourceWindowHandle))
            CompleteCoordinatedMove(move.SourceWindowHandle, hasMoved: true);
        if (_magneticResizeSession is { } resize && windows.Contains(resize.Source))
        {
            CompleteMagneticResize(resize.Source);
            CompleteLayoutOperation(resize.Source.Config.Id);
        }
        _magneticMembershipRevisions[id] = MagneticMembershipRevision(id) + 1;
        _pendingMagneticCollapse.Remove(id);
        foreach (var window in windows)
        {
            if (!_committingMagneticDock && window != continuingDrag)
            {
                if (window.InterruptMagneticDrag()) CompleteLayoutOperation(window.Config.Id);
            }
            CancelMouseLeaveAutoCollapse(window, wholeScope: false);
            CancelWidgetGroupDrag(window.Config.Id);
            window.SetGroupDropPreview(false, false);
            window.ResetMagneticInteractionState();
        }
    }

    private void NormalizeMagneticCollapseMembership(WidgetConfig config)
    {
        if (GetMagneticGroupId(config) is not { } id) return;
        var changed = _settingsService.Settings.WidgetLayout.Widgets.Where(member =>
            GetMagneticGroupId(member) == id && !member.MagneticCollapseLinked &&
            member.Metadata.GetValueOrDefault(MagneticIndependentCollapseKey) != "true").ToArray();
        foreach (var member in changed) member.MagneticCollapseLinked = true;
        if (changed.Length == 0) return;
        _settingsService.UpdateWidgetsBatch(changed, notifySubscribers: false);
        _settingsService.SaveDebounced(notifySubscribers: false);
    }

    internal bool DissolveMagneticGroup(WidgetConfig config)
    {
        if (GetMagneticGroupId(config) is not { } id || IsMagneticLayoutLocked(config)) return false;
        InvalidateMagneticMembership(id);
        using var operation = TrackLayoutOperation(config.Id, "Magnetic.Undo.Split");
        var members = _settingsService.Settings.WidgetLayout.Widgets.Where(member => GetMagneticGroupId(member) == id).ToArray();
        ReplaceMagneticGroupId(id, null, invalidate: false);
        _settingsService.UpdateWidgetsBatch(members, notifySubscribers: false);
        _settingsService.SaveDebounced(notifySubscribers: false);
        foreach (var window in GetLoadedDesktopWindows().OfType<WidgetWindowBase>().Where(window => members.Contains(window.Config)))
            ArmMouseLeaveAutoCollapse(window);
        RefreshMouseLeaveMembership();
        return true;
    }
}
