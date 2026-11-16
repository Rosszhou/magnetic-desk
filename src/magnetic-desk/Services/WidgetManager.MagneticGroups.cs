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
        if (id is null) DetachMagneticCollapse(config);
        WidgetGroupConfig? surface = WidgetGroupSettings.FindByMember(_settingsService.Settings, config.Id);
        if (surface is not null)
        {
            surface.MagneticGroupId = id;
            foreach (WidgetConfig member in _settingsService.Settings.Widgets.Where(w => surface.MemberIds.Contains(w.Id)))
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
    }

    internal void AppendMagneticGroupMenu(MenuFlyout flyout, WidgetConfig config)
    {
        AppendCapsuleAppearanceMenu(flyout, config);
        LocalizationService localization = App.Current.LocalizationService;
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
        if (WidgetCollapseBehaviorNames.Resolve(config, _settingsService.Settings.WidgetCollapseBehavior) != WidgetCollapseBehavior.Expanded)
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
                IsEnabled = !config.IsPositionLocked && target.CanParticipateInCoordinatedMove
            };
            item.Click += (_, _) => JoinMagneticGroup(config.Id, targetId);
            join.Items.Add(item);
        }
        join.IsEnabled = join.Items.Count > 0 && _coordinatedMoveSession is null;
        menu.Items.Add(join);
        if (groupId is not null)
        {
            var members = new MenuFlyoutSubItem { Text = localization.T("Magnetic.Members") };
            foreach (IDesktopWidgetWindow member in GetLoadedDesktopWindows().Where(w => GetMagneticGroupId(w.Config) == groupId))
            {
                members.Items.Add(new MenuFlyoutItem { Text = member.Config.Name, IsEnabled = false });
            }
            menu.Items.Add(members);
            var leave = new MenuFlyoutItem { Text = localization.T("Magnetic.Leave") };
            leave.Click += (_, _) =>
            {
                LeaveMagneticGroup(config);
            };
            menu.Items.Add(leave);
            var dissolve = new MenuFlyoutItem { Text = localization.T("Magnetic.Dissolve") };
            dissolve.Click += (_, _) =>
            {
                ReplaceMagneticGroupId(groupId, null);
                _settingsService.SaveDebounced(notifySubscribers: false);
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

    internal bool LeaveMagneticGroup(WidgetConfig config)
    {
        string? previous = GetMagneticGroupId(config);
        if (previous is null) return false;
        SetMagneticGroupId(config, null);
        var remaining = _settingsService.Settings.Widgets.Where(w => !IsDeleted(w.Id) && GetMagneticGroupId(w) == previous)
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

    private void ReplaceMagneticGroupId(string oldId, string? newId)
    {
        foreach (WidgetConfig widget in _settingsService.Settings.Widgets.Where(w => w.MagneticGroupId == oldId))
        {
            if (newId is null)
            {
                DetachMagneticCollapse(widget);
                widget.MagneticAttachmentPlacement = null;
                widget.MagneticAttachmentAnchor = null;
            }
            widget.MagneticGroupId = newId;
            if (newId is null) widget.MagneticCollapseLinked = false;
        }
        foreach (WidgetGroupConfig group in _settingsService.Settings.WidgetGroups.Where(g => g.MagneticGroupId == oldId))
        {
            group.MagneticGroupId = newId;
        }
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

        RectInt32 bounds = source.CoordinatedMoveBounds;
        double dpiScale = Win32Helper.GetDpiScaleForWindow(target.WindowHandle, target.WindowContentRoot?.XamlRoot);
        RectInt32 destination = WidgetMagneticPlacement.SnapBeside(
            bounds, target.CoordinatedMoveBounds,
            Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetSnapSpacing * dpiScale)));
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

        string joinedId = LinkMagneticGroups(source.Config, target.Config);
        _settingsService.UpdateWidgetsBatch(moving.Select(w => w.Config), notifySubscribers: false);
        NoteWidgetWindowsMoved();
        App.Log($"[MagneticGroup] Joined source={sourceId} target={targetId} group={joinedId}");
    }

    private string LinkMagneticGroups(WidgetConfig source, WidgetConfig target)
    {
        string? sourceGroup = GetMagneticGroupId(source);
        string? targetGroup = GetMagneticGroupId(target);
        var targetMembers = targetGroup is null ? [target] :
            _settingsService.Settings.Widgets.Where(w => GetMagneticGroupId(w) == targetGroup).ToArray();
        WidgetConfig collapseTarget = targetMembers.FirstOrDefault(w => w.MagneticCollapseLinked)
            ?? targetMembers.FirstOrDefault(w => WidgetCollapseBehaviorNames.Resolve(
                w, _settingsService.Settings.WidgetCollapseBehavior) is WidgetCollapseBehavior.Click or WidgetCollapseBehavior.Smart)
            ?? target;
        WidgetCollapseBehavior behavior = WidgetCollapseBehaviorNames.Resolve(
            collapseTarget, _settingsService.Settings.WidgetCollapseBehavior);
        bool linked = targetGroup is not null && (collapseTarget.MagneticCollapseLinked ||
            behavior is WidgetCollapseBehavior.Click or WidgetCollapseBehavior.Smart);
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
        var visibleBounds = joinedWindows.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
        var expandedSizes = joinedWindows.ToDictionary(w => w.Config.Id, w =>
        {
            var bounds = w.Config.MagneticExpandedPlacement?.ToBounds() ?? w.MagneticExpandedBounds;
            return new SizeInt32(bounds.Width, bounds.Height);
        });
        if (visibleBounds.Count > 0)
        {
            var origin = WidgetCoordinatedMoveCalculator.GetUnion(visibleBounds.Values);
            int gap = Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetSnapSpacing *
                Win32Helper.GetDpiScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(joinedWindows.First(w => w.Config.Id == target.Id)),
                    joinedWindows.First(w => w.Config.Id == target.Id).WindowContentRoot?.XamlRoot)));
            var expandedBounds = WidgetMagneticLayout.Capture(visibleBounds).Arrange(expandedSizes, origin.X, origin.Y, gap);
            foreach (var window in joinedWindows)
            {
                foreach (var member in MagneticSurfaceConfigs(window.Config))
                {
                    member.MagneticCollapseLinked = linked;
                    member.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(expandedBounds[window.Config.Id]);
                    member.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(expandedBounds[window.Config.Id]);
                    member.MagneticAttachmentAnchor = target.PositionAnchor;
                    member.MagneticCompactPlacement = null;
                }
                window.ResetMagneticInteractionState();
            }
        }

        if (linked)
        {
            foreach (var member in _settingsService.Settings.Widgets.Where(w => GetMagneticGroupId(w) == joinedId))
            {
                member.MagneticCollapseLinked = true;
                WidgetCollapseBehaviorNames.SetOverride(member, behavior);
                SetWidgetGroupCollapseBehavior(member, behavior);
            }
            var windows = MagneticCollapseWindows(target);
            foreach (var window in windows) window.RefreshMagneticCollapseBehavior();
            if (windows.Any(w => w.IsCompactArrangementActive))
                ApplyMagneticCollapse(windows, windows.All(w => w.IsCompactArrangementActive), persist: true, animate: true);
            foreach (var window in windows.Where(w => !w.IsCompactArrangementActive)) ArmMouseLeaveAutoCollapse(window);
        }
        RestoreMagneticCollapseGroups();
        RefreshMouseLeaveMembership();
        _settingsService.SaveDebounced(notifySubscribers: false);
        return joinedId;
    }
}
