using MagneticDesk.Models;
using MagneticDesk.Platform;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private string? GetMagneticGroupId(WidgetConfig config)
    {
        WidgetGroupConfig? surface = WidgetGroupSettings.FindByMember(_settingsService.Settings, config.Id);
        string? id = surface is null ? config.MagneticGroupId : surface.MagneticGroupId;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private void SetMagneticGroupId(WidgetConfig config, string? id)
    {
        if (GetMagneticGroupId(config) != id)
            foreach (var member in MagneticSurfaceConfigs(config))
            {
                member.MagneticCompactPlacement = null;
                member.MagneticAttachmentPlacement = null;
                member.MagneticAttachmentAnchor = null;
            }
        if (id is null) DetachMagneticCollapse(config, independent: false);
        WidgetGroupConfig? surface = WidgetGroupSettings.FindByMember(_settingsService.Settings, config.Id);
        if (surface is not null)
        {
            surface.MagneticGroupId = id;
            foreach (WidgetConfig member in _settingsService.Settings.WidgetLayout.Widgets.Where(w => surface.MemberIds.Contains(w.Id)))
            {
                member.MagneticGroupId = id;
                if (id is null) member.MagneticCollapseLinked = false;
            }
        }
        else
        {
            config.MagneticGroupId = id;
            if (id is null) config.MagneticCollapseLinked = false;
        }
        RefreshMagneticGroupChrome();
    }

    private void RefreshMagneticGroupChrome()
    {
        foreach (var window in GetLoadedDesktopWindows().OfType<MagneticDesk.Views.ContentWidgetWindow>())
            window.RefreshMagneticGroupChrome();
    }

    internal MenuFlyout CreateMagneticGroupActions(WidgetConfig config)
    {
        var source = new MenuFlyout();
        AppendMagneticGroupMenu(source, config);
        var result = new MenuFlyout();
        var undo = source.Items.OfType<MenuFlyoutItem>().First(item => item.KeyboardAcceleratorTextOverride == "Ctrl+Z");
        source.Items.Remove(undo);
        result.Items.Add(undo);
        var group = source.Items.OfType<MenuFlyoutSubItem>().First(item => item.Text == _localizationService.T("Magnetic.Menu"));
        string[] actions = ["Magnetic.LockLayout", "Magnetic.Leave", "Magnetic.Dissolve"];
        foreach (var item in group.Items.ToArray())
        {
            string? text = item is MenuFlyoutItem command ? command.Text : null;
            if (text is null || !actions.Any(key => _localizationService.T(key) == text)) continue;
            group.Items.Remove(item);
            result.Items.Add(item);
        }
        return result;
    }

    internal void AppendMagneticGroupMenu(MenuFlyout flyout, WidgetConfig config)
    {
        AppendCapsuleAppearanceMenu(flyout, config);
        LocalizationService localization = _localizationService;
        var undo = new MenuFlyoutItem
        {
            Text = localization.T("Magnetic.Undo") + (_layoutUndoHistory.Count > 0
                ? " · " + localization.T(_layoutUndoHistory[^1].Description) : ""),
            Icon = new FontIcon { Glyph = "\uE7A7" },
            KeyboardAcceleratorTextOverride = "Ctrl+Z",
            IsEnabled = CanUndoLayout
        };
        undo.Click += (_, _) => UndoLayout();
        flyout.Items.Add(undo);
        string? groupId = GetMagneticGroupId(config);
        var menu = new MenuFlyoutSubItem
        {
            Text = localization.T("Magnetic.Menu"),
            Icon = new FontIcon { Glyph = "\uE71B" }
        };
        var animations = new MenuFlyoutSubItem { Text = localization.T("Magnetic.Animation.Menu") };
        MagneticAnimationStyle selectedStyle = MagneticDockAnimation.Normalize(config.MagneticAnimationStyle);
        foreach (MagneticAnimationStyle style in Enum.GetValues<MagneticAnimationStyle>())
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = localization.T($"Magnetic.Animation.{style}"),
                IsChecked = selectedStyle == style
            };
            item.Click += (_, _) =>
            {
                config.MagneticAnimationStyle = style.ToString();
                _settingsService.UpdateWidget(config, notifySubscribers: false);
                _settingsService.SaveDebounced(notifySubscribers: false);
            };
            animations.Items.Add(item);
        }
        menu.Items.Add(animations);
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = localization.T(groupId is null ? "Magnetic.Free" : "Magnetic.Hint"),
            IsEnabled = false
        });
        if (WidgetCollapseBehaviorNames.Resolve(config, _settingsService.Settings.WidgetShell.WidgetCollapseBehavior) != WidgetCollapseBehavior.Expanded)
            menu.Items.Add(new MenuFlyoutItem { Text = localization.T("Magnetic.MouseLeave.Hint"), IsEnabled = false });
        var join = new MenuFlyoutSubItem { Text = localization.T("Magnetic.Join") };
        foreach (IDesktopWidgetWindow target in GetLoadedDesktopWindows())
        {
            if (!target.Visible || target.Config.Id == config.Id ||
                (groupId is not null && GetMagneticGroupId(target.Config) == groupId))
            {
                continue;
            }
            string targetId = target.Config.Id;
            var item = new MenuFlyoutItem
            {
                Text = target.Config.Name,
                IsEnabled = !config.IsPositionLocked && !IsMagneticLayoutLocked(config) && target.CanParticipateInCoordinatedMove
            };
            item.Click += (_, _) => JoinMagneticGroup(config.Id, targetId);
            join.Items.Add(item);
        }
        join.IsEnabled = join.Items.Count > 0 && _coordinatedMoveSession is null;
        menu.Items.Add(join);
        if (groupId is not null)
        {
            bool layoutLocked = IsMagneticLayoutLocked(config);
            var layoutLock = new ToggleMenuFlyoutItem
            {
                Text = localization.T("Magnetic.LockLayout"),
                IsChecked = layoutLocked,
                IsEnabled = _layoutOperation is null
            };
            layoutLock.Click += (_, _) =>
            {
                if (!SetMagneticLayoutLocked(config, layoutLock.IsChecked))
                    layoutLock.IsChecked = IsMagneticLayoutLocked(config);
            };
            menu.Items.Add(layoutLock);
            var members = new MenuFlyoutSubItem { Text = localization.T("Magnetic.Members") };
            foreach (IDesktopWidgetWindow member in GetLoadedDesktopWindows().Where(w => GetMagneticGroupId(w.Config) == groupId))
            {
                members.Items.Add(new MenuFlyoutItem { Text = member.Config.Name, IsEnabled = false });
            }
            menu.Items.Add(members);
            var leave = new MenuFlyoutItem { Text = localization.T("Magnetic.Leave"), IsEnabled = !layoutLocked };
            leave.Click += (_, _) =>
            {
                LeaveMagneticGroup(config);
            };
            menu.Items.Add(leave);
            var dissolve = new MenuFlyoutItem { Text = localization.T("Magnetic.Dissolve"), IsEnabled = !layoutLocked };
            dissolve.Click += (_, _) =>
            {
                DissolveMagneticGroup(config);
            };
            menu.Items.Add(dissolve);
        }
        var groupMenu = new MenuFlyout();
        WidgetGroupMenuBuilder.Append(groupMenu, config, this, localization);
        foreach (var item in groupMenu.Items.ToArray())
        {
            groupMenu.Items.Remove(item);
            menu.Items.Add(item);
        }
        flyout.Items.Add(menu);
    }

    internal bool LeaveMagneticGroup(WidgetConfig config, bool continueDrag = false)
    {
        string? previous = GetMagneticGroupId(config);
        if (previous is null || IsMagneticLayoutLocked(config)) return false;
        var continuingDrag = continueDrag ? GetLoadedDesktopWindows().OfType<MagneticDesk.Views.WidgetWindowBase>()
            .FirstOrDefault(window => window.Config.Id == config.Id) : null;
        InvalidateMagneticMembership(previous, continuingDrag);
        using var operation = TrackLayoutOperation(config.Id, "Magnetic.Undo.Split");
        SetMagneticGroupId(config, null);
        var remaining = _settingsService.Settings.WidgetLayout.Widgets.Where(w => !IsDeleted(w.Id) && GetMagneticGroupId(w) == previous)
            .GroupBy(w => WidgetGroupSettings.FindByMember(_settingsService.Settings, w.Id)?.SurfaceId ?? w.Id)
            .Select(group => group.First()).ToArray();
        if (remaining.Length == 1) SetMagneticGroupId(remaining[0], null);
        _settingsService.UpdateWidgetsBatch(remaining.Append(config), notifySubscribers: false);
        _settingsService.SaveDebounced(notifySubscribers: false);
        App.Log($"[MagneticGroup] Detached widget={config.Id} remaining={remaining.Length}");
        RefreshMouseLeaveMembership();
        return true;
    }

    internal string[] GetMagneticCompanionIds(WidgetConfig config)
    {
        string? id = GetMagneticGroupId(config);
        return id is null ? [] : GetLoadedDesktopWindows()
            .Where(w => w.Config.Id != config.Id && GetMagneticGroupId(w.Config) == id)
            .Select(w => w.Config.Id).ToArray();
    }

    private void ReplaceMagneticGroupId(string oldId, string? newId, bool invalidate = true)
    {
        if (invalidate) InvalidateMagneticMembership(oldId);
        foreach (WidgetConfig widget in _settingsService.Settings.WidgetLayout.Widgets.Where(w => w.MagneticGroupId == oldId))
        {
            if (newId is null)
            {
                DetachMagneticCollapse(widget, independent: false);
                widget.MagneticAttachmentPlacement = null;
                widget.MagneticAttachmentAnchor = null;
            }
            widget.MagneticGroupId = newId;
            if (newId is null) widget.MagneticCollapseLinked = false;
        }
        foreach (WidgetGroupConfig group in _settingsService.Settings.WidgetLayout.WidgetGroups.Where(g => g.MagneticGroupId == oldId))
        {
            group.MagneticGroupId = newId;
        }
        RefreshMagneticGroupChrome();
    }

    private void JoinMagneticGroup(string sourceId, string targetId)
    {
        if (_coordinatedMoveSession is not null)
        {
            return;
        }
        IReadOnlyList<IDesktopWidgetWindow> windows = GetLoadedDesktopWindows();
        IDesktopWidgetWindow? source = windows.FirstOrDefault(w => w.Config.Id == sourceId);
        IDesktopWidgetWindow? target = windows.FirstOrDefault(w => w.Config.Id == targetId);
        if (source is null || target is null || source == target || !target.Visible)
        {
            return;
        }
        if (IsMagneticLayoutLocked(source.Config) || IsMagneticLayoutLocked(target.Config)) return;
        ClearMagneticDockFeedback();
        using var operation = TrackLayoutOperation(sourceId, "Magnetic.Undo.Join");
        IncludeLayoutOperationMembers(targetId);
        string? sourceGroup = GetMagneticGroupId(source.Config);
        string? targetGroup = GetMagneticGroupId(target.Config);
        if (sourceGroup is not null && sourceGroup == targetGroup)
        {
            return;
        }
        IDesktopWidgetWindow[] moving = windows.Where(w => w.Visible &&
            (w == source || (sourceGroup is not null && GetMagneticGroupId(w.Config) == sourceGroup))).ToArray();
        if (moving.Any(w => !w.CanParticipateInCoordinatedMove))
        {
            return;
        }

        var compactMembers = windows.OfType<MagneticDesk.Views.WidgetWindowBase>().Where(window => window.Visible &&
            window.IsCompactArrangementActive && (moving.Contains((IDesktopWidgetWindow)window) || ReferenceEquals(window, target) ||
                targetGroup is not null && GetMagneticGroupId(window.Config) == targetGroup)).ToArray();
        bool joined = false;
        try
        {
            foreach (var group in compactMembers.GroupBy(window => GetMagneticGroupId(window.Config) ?? window.Config.Id))
                ApplyMagneticCollapse(group.ToArray(), false, false, false);
            RectInt32 bounds = source.CoordinatedMoveBounds;
            double dpiScale = Win32Helper.GetDpiScaleForWindow(target.WindowHandle, target.WindowContentRoot?.XamlRoot);
            var movingIds = moving.Select(window => window.Config.Id).ToHashSet(StringComparer.Ordinal);
            var placement = WidgetMagneticDockPlacement.FindBeside(
                bounds, target.CoordinatedMoveBounds,
                windows.Where(window => window.Visible && !movingIds.Contains(window.Config.Id)).SelectMany(MagneticDockOccupancy).ToArray(),
                moving.SelectMany(MagneticDockOccupancy).ToArray(),
                Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetShell.WidgetSnapSpacing * dpiScale)),
                Math.Max(1, (int)Math.Round(28 * dpiScale)));
            if (placement is null) return;
            RectInt32 destination = placement.Value.DockedBounds;
            int deltaX = destination.X - bounds.X;
            int deltaY = destination.Y - bounds.Y;
            foreach (IDesktopWidgetWindow window in moving)
            {
                RectInt32 current = window.CoordinatedMoveBounds;
                window.BeginCoordinatedMoveParticipation(isSource: false);
                window.ApplyCoordinatedMoveBoundsFallback(new RectInt32(
                    current.X + deltaX, current.Y + deltaY, current.Width, current.Height));
                window.CompleteCoordinatedMoveParticipation(hasMoved: true, isSource: false);
            }

            string joinedId;
            _committingMagneticDock = true;
            try { joinedId = LinkMagneticGroups(source.Config, target.Config); }
            finally { _committingMagneticDock = false; }
            joined = true;
            _settingsService.UpdateWidgetsBatch(moving.Select(w => w.Config), notifySubscribers: false);
            NoteWidgetWindowsMoved();
            App.Log($"[MagneticGroup] Joined source={sourceId} target={targetId} group={joinedId}");
        }
        finally
        {
            if (!joined)
                foreach (var group in compactMembers.GroupBy(window => GetMagneticGroupId(window.Config) ?? window.Config.Id))
                    ApplyMagneticCollapse(group.ToArray(), true, false, false);
        }
    }

    private string LinkMagneticGroups(WidgetConfig source, WidgetConfig target)
    {
        IncludeLayoutOperationMembers(source.Id);
        IncludeLayoutOperationMembers(target.Id);
        string? sourceGroup = GetMagneticGroupId(source);
        string? targetGroup = GetMagneticGroupId(target);
        WidgetCollapseBehavior behavior = WidgetCollapseBehaviorNames.Resolve(
            target, _settingsService.Settings.WidgetShell.WidgetCollapseBehavior);
        if (targetGroup is not null) InvalidateMagneticMembership(targetGroup);
        string joinedId = GetMagneticGroupId(target) ?? sourceGroup ?? Guid.NewGuid().ToString();
        if (sourceGroup is not null)
        {
            ReplaceMagneticGroupId(sourceGroup, joinedId);
        }
        SetMagneticGroupId(source, joinedId);
        SetMagneticGroupId(target, joinedId);
        SetCapsuleAppearance(target, WidgetCapsuleAppearance.Normalize(target.CapsuleStyle));
        var joinedWindows = GetLoadedDesktopWindows().OfType<MagneticDesk.Views.WidgetWindowBase>()
            .Where(w => w.Visible && GetMagneticGroupId(w.Config) == joinedId).ToArray();
        var expandedBounds = joinedWindows.ToDictionary(w => w.Config.Id, w => w.IsCompactArrangementActive
            ? w.Config.MagneticExpandedPlacement?.ToBounds() ?? w.MagneticExpandedBounds
            : w.CoordinatedMoveBounds);
        foreach (var window in joinedWindows)
        {
            foreach (var member in MagneticSurfaceConfigs(window.Config))
            {
                member.MagneticCollapseLinked = true;
                member.Metadata.Remove(MagneticIndependentCollapseKey);
                member.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(expandedBounds[window.Config.Id]);
                member.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(expandedBounds[window.Config.Id]);
                member.MagneticAttachmentAnchor = target.PositionAnchor;
                member.MagneticCompactPlacement = null;
            }
            window.ResetMagneticInteractionState();
        }

        foreach (var member in _settingsService.Settings.WidgetLayout.Widgets.Where(w => GetMagneticGroupId(w) == joinedId))
        {
            member.MagneticCollapseLinked = true;
            member.Metadata.Remove(MagneticIndependentCollapseKey);
            WidgetCollapseBehaviorNames.SetOverride(member, behavior);
            SetWidgetGroupCollapseBehavior(member, behavior);
        }
        var windows = MagneticCollapseWindows(target);
        foreach (var window in windows) window.RefreshMagneticCollapseBehavior();
        if (windows.Any(w => w.IsCompactArrangementActive))
            ApplyMagneticCollapse(windows, windows.All(w => w.IsCompactArrangementActive), persist: true, animate: true);
        foreach (var window in windows.Where(w => !w.IsCompactArrangementActive)) ArmMouseLeaveAutoCollapse(window);
        RestoreMagneticCollapseGroups();
        RefreshMouseLeaveMembership();
        _settingsService.SaveDebounced(notifySubscribers: false);
        return joinedId;
    }
}
