using MagneticDesk.Models;
using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private bool _applyingMagneticCollapse;
    private readonly HashSet<string> _pendingMagneticCollapse = new(StringComparer.Ordinal);

    private Dictionary<string, RectInt32> MagneticAttachmentBounds(WidgetWindowBase[] windows)
    {
        var reference = windows.ToDictionary(w => w.Config.Id, w =>
            (w.Config.MagneticAttachmentPlacement ?? w.Config.MagneticExpandedPlacement)?.ToBounds() ?? w.MagneticExpandedBounds);
        foreach (var window in windows)
            foreach (var config in MagneticSurfaceConfigs(window.Config))
                config.MagneticAttachmentPlacement ??= MagneticExpandedPlacement.FromBounds(reference[window.Config.Id]);
        return reference;
    }

    private string? ResolveMagneticAttachmentAnchor(WidgetWindowBase[] windows)
    {
        string? anchor = windows.Select(w => w.Config.MagneticAttachmentAnchor).FirstOrDefault(a => a is not null)
            ?? windows[0].Config.PositionAnchor;
        foreach (var window in windows)
            foreach (var config in MagneticSurfaceConfigs(window.Config)) config.MagneticAttachmentAnchor ??= anchor;
        return anchor;
    }

    internal bool HasMagneticGroup(WidgetConfig config) => GetMagneticGroupId(config) is not null;

    private WidgetWindowBase[] MagneticCollapseWindows(WidgetConfig config) =>
        GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.Visible && w.MagneticCollapseInitialized && w.Config.MagneticCollapseLinked &&
                GetMagneticGroupId(w.Config) == GetMagneticGroupId(config))
            .ToArray();

    internal void DetachMagneticCollapse(WidgetConfig config, bool independent = true)
    {
        if (independent && GetMagneticGroupId(config) is { } id) InvalidateMagneticMembership(id);
        foreach (var member in MagneticSurfaceConfigs(config))
        {
            member.MagneticCollapseLinked = false;
            if (independent && GetMagneticGroupId(config) is not null) member.Metadata[MagneticIndependentCollapseKey] = "true";
            else member.Metadata.Remove(MagneticIndependentCollapseKey);
        }
        var window = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().FirstOrDefault(w => w.Config.Id == config.Id);
        if (window is not null)
        {
            CancelMouseLeaveAutoCollapse(window, wholeScope: false);
            window.DetachMagneticCollapseLayout();
            window.ResetMagneticInteractionState();
        }
        foreach (var member in MagneticSurfaceConfigs(config)) member.MagneticCompactPlacement = null;
        foreach (var member in MagneticSurfaceConfigs(config))
            member.MagneticExpandedPlacement = config.MagneticExpandedPlacement;
    }

    private IEnumerable<WidgetConfig> MagneticSurfaceConfigs(WidgetConfig config)
    {
        var surface = WidgetGroupSettings.FindByMember(_settingsService.Settings, config.Id);
        return surface is null ? new[] { config } :
            _settingsService.Settings.WidgetLayout.Widgets.Where(w => surface.MemberIds.Contains(w.Id));
    }

    internal void SetMagneticGroupCollapseBehavior(WidgetConfig config, WidgetCollapseBehavior behavior)
    {
        string? id = GetMagneticGroupId(config);
        if (id is null) return;
        InvalidateMagneticMembership(id);
        foreach (var member in _settingsService.Settings.WidgetLayout.Widgets.Where(w => GetMagneticGroupId(w) == id))
        {
            member.MagneticCollapseLinked = true;
            member.Metadata.Remove(MagneticIndependentCollapseKey);
            WidgetCollapseBehaviorNames.SetOverride(member, behavior);
            var surface = WidgetGroupSettings.FindByMember(_settingsService.Settings, member.Id);
            if (surface is not null)
                surface.CollapseBehavior = WidgetCollapseBehaviorNames.ToSettingValue(behavior);
        }
        var windows = MagneticCollapseWindows(config);
        foreach (var window in windows)
        {
            SetWidgetGroupCollapseBehavior(window.Config, behavior);
            window.RefreshMagneticCollapseBehavior();
        }
        bool collapsed = WidgetCollapseBehaviorNames.Resolve(config,
            _settingsService.Settings.WidgetShell.WidgetCollapseBehavior) != WidgetCollapseBehavior.Expanded;
        foreach (var member in _settingsService.Settings.WidgetLayout.Widgets.Where(w => GetMagneticGroupId(w) == id))
            member.IsCollapsed = collapsed;
        foreach (var surface in _settingsService.Settings.WidgetLayout.WidgetGroups.Where(g => g.MagneticGroupId == id))
            surface.IsCollapsed = collapsed;
        ApplyMagneticCollapse(windows, collapsed, persist: true, animate: true);
        _settingsService.SaveDebounced(notifySubscribers: false);
        RefreshCapsuleBarLayout();
    }

    internal bool TrySetMagneticGroupCollapsed(WidgetWindowBase source, bool collapsed,
        bool persist, bool animate, bool allowDuringInteraction)
    {
        if (_applyingMagneticCollapse ||
            GetMagneticGroupId(source.Config) is not { } id) return false;
        NormalizeMagneticCollapseMembership(source.Config);
        var windows = MagneticCollapseWindows(source.Config);
        if (!source.Config.MagneticCollapseLinked || windows.Length < 2)
        {
            if (source.IsCompactArrangementActive == collapsed)
            {
                if (persist)
                {
                    source.Config.IsCollapsed = collapsed;
                    _settingsService.UpdateWidget(source.Config, notifySubscribers: false);
                }
                return true;
            }
            var members = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
                .Where(w => w.Visible && w.MagneticCollapseInitialized && GetMagneticGroupId(w.Config) == id).ToArray();
            if (members.Length < 2) return false;
            ApplyMagneticCollapse([source], collapsed, persist, animate);
            return true;
        }
        if (collapsed && !allowDuringInteraction && windows.Any(HasMouseLeaveAutoCollapse)) return true;
        if (collapsed && !allowDuringInteraction && windows.Any(w => w.MagneticSmartPinnedOpen)) return true;
        if (collapsed && !allowDuringInteraction && windows.Any(w => w.MagneticGroupInteractionActive))
        {
            if (_pendingMagneticCollapse.Add(id))
                _ = RetryMagneticCollapseAsync(source, id, MagneticMembershipRevision(id));
            return true;
        }
        _pendingMagneticCollapse.Remove(id);
        if (windows.All(w => w.IsCompactArrangementActive == collapsed))
        {
            if (persist)
            {
                foreach (var window in windows) window.Config.IsCollapsed = collapsed;
                _settingsService.UpdateWidgetsBatch(windows.Select(w => w.Config), notifySubscribers: false);
            }
            return true;
        }
        ApplyMagneticCollapse(windows, collapsed, persist, animate, source.MagneticSmartPinnedOpen);
        return true;
    }

    private async Task RetryMagneticCollapseAsync(WidgetWindowBase source, string id, long revision)
    {
        await Task.Delay(150);
        if (GetMagneticGroupId(source.Config) != id || MagneticMembershipRevision(id) != revision ||
            !_pendingMagneticCollapse.Remove(id) || !source.MagneticCollapseInitialized) return;
        if (WidgetCollapseBehaviorNames.Resolve(source.Config,
            _settingsService.Settings.WidgetShell.WidgetCollapseBehavior) != WidgetCollapseBehavior.Smart) return;
        TrySetMagneticGroupCollapsed(source, true, false, true, false);
    }

    private void ApplyMagneticCollapse(WidgetWindowBase[] windows, bool collapsed, bool persist, bool animate,
        bool pinnedOpen = false, bool restoreOnly = false)
    {
        if (windows.Length == 0) return;
        using var boundsPreparation = WidgetCompactAnimationCoordinator.PrepareBoundsBatch();
        _applyingMagneticCollapse = true;
        try
        {
            string? id = GetMagneticGroupId(windows[0].Config);
            var members = id is null ? windows : GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
                .Where(w => w.Visible && w.MagneticCollapseInitialized && GetMagneticGroupId(w.Config) == id).ToArray();
            var changed = windows.ToHashSet();
            var states = members.ToDictionary(w => w.Config.Id,
                w => !restoreOnly && changed.Contains(w) ? collapsed : w.IsCompactArrangementActive);
            foreach (var member in members)
                member.Config.MagneticExpandedPlacement ??= MagneticExpandedPlacement.FromBounds(member.MagneticExpandedBounds);
            var expanded = members.ToDictionary(w => w.Config.Id, w => w.Config.MagneticExpandedPlacement!.ToBounds());
            var attachment = MagneticAttachmentBounds(members);
            string? anchor = ResolveMagneticAttachmentAnchor(members);
            double scale = Platform.Win32Helper.GetDpiScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(members[0]), members[0].WindowContentRoot?.XamlRoot);
            int expandedGap = Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetShell.WidgetSnapSpacing * scale));
            int tolerance = Math.Max(1, (int)Math.Round(12 * scale));
            var compactSizes = members.ToDictionary(w => w.Config.Id, w => w.MagneticCompactSize);
            var homes = members.Where(w => w.Config.MagneticCompactPlacement is not null)
                .ToDictionary(w => w.Config.Id, w => w.Config.MagneticCompactPlacement!.ToBounds());
            var compactHomes = WidgetMagneticCollapseLayout.RestoreCompact(expanded, homes, compactSizes, anchor, expandedGap, tolerance);
            compactHomes = WidgetMagneticCollapseLayout.KeepVisible(compactHomes, members[0].MagneticWorkArea);
            foreach (var member in members)
                member.Config.MagneticCompactPlacement = MagneticExpandedPlacement.FromBounds(compactHomes[member.Config.Id]);
            var homeBounds = WidgetCoordinatedMoveCalculator.GetUnion(compactHomes.Values);
            var origin = new PointInt32(homeBounds.X, homeBounds.Y);
            expanded = WidgetMagneticCollapseLayout.KeepVisible(
                WidgetMagneticCollapseLayout.RestoreExpanded(expanded, origin, attachment, anchor, expandedGap, tolerance),
                members[0].MagneticWorkArea).ToDictionary();
            foreach (var member in members)
                member.Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(expanded[member.Config.Id]);
            var sizes = members.ToDictionary(w => w.Config.Id, w => states[w.Config.Id]
                ? compactSizes[w.Config.Id] : new SizeInt32(expanded[w.Config.Id].Width, expanded[w.Config.Id].Height));
            var targets = states.Values.Any(value => value)
                ? WidgetMagneticCollapseLayout.ArrangeTransition(expanded, sizes, anchor, expandedGap, tolerance)
                : expanded;
            if (states.Values.All(value => value)) targets = compactHomes;
            targets = WidgetMagneticCollapseLayout.KeepVisible(targets, members[0].MagneticWorkArea);
            // Clear space before the opening surface starts its bounds transition.
            foreach (var member in members.Where(w => restoreOnly || !changed.Contains(w)))
                member.ApplyMagneticReflowBounds(targets[member.Config.Id], animate && !restoreOnly);
            foreach (var member in members)
            {
                if (!restoreOnly && changed.Contains(member))
                    member.ApplyMagneticCollapsedState(collapsed, targets[member.Config.Id], persist, animate, pinnedOpen);
                foreach (var config in MagneticSurfaceConfigs(member.Config))
                {
                    config.MagneticExpandedPlacement = member.Config.MagneticExpandedPlacement;
                    config.MagneticCompactPlacement = member.Config.MagneticCompactPlacement;
                }
            }
            _settingsService.UpdateWidgetsBatch(members.Select(w => w.Config), notifySubscribers: false);
        }
        finally { _applyingMagneticCollapse = false; }
    }

    private void RestoreMagneticCollapseGroups()
    {
        if (_applyingMagneticCollapse || _magneticResizeSession is not null) return;
        foreach (var group in GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.MagneticCollapseInitialized && w.Visible)
            .GroupBy(w => GetMagneticGroupId(w.Config)).Where(g => g.Key is not null))
        {
            var windows = group.ToArray();
            NormalizeMagneticCollapseMembership(windows[0].Config);
            if (windows.Length > 1 && windows.Any(w => w.IsCompactArrangementActive) &&
                !windows.Any(w => w.MagneticBoundsTransitionActive))
            {
                bool fullyLinked = windows.All(window => window.Config.MagneticCollapseLinked);
                ApplyMagneticCollapse(windows, windows.All(window => window.IsCompactArrangementActive), false, false, restoreOnly: !fullyLinked);
            }
        }
    }
}
