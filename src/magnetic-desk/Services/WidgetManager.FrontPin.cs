using MagneticDesk.Models;
using MagneticDesk.Views;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal MenuFlyout CreateFrontPinMenu(WidgetConfig config)
    {
        var menu = new MenuFlyout();
        var current = new ToggleMenuFlyoutItem { Text = _localizationService.T("FrontPin.Current"), IsChecked = config.IsAlwaysOnTop };
        current.Click += (_, _) => SetPinnedToFront(config, current.IsChecked, false);
        menu.Items.Add(current);
        if (GetMagneticGroupId(config) is { } group)
        {
            var whole = new ToggleMenuFlyoutItem { Text = _localizationService.T("FrontPin.Group"),
                IsChecked = _settingsService.Settings.Widgets.Where(w => GetMagneticGroupId(w) == group).All(w => w.IsAlwaysOnTop) };
            whole.Click += (_, _) => SetPinnedToFront(config, whole.IsChecked, true);
            menu.Items.Add(whole);
        }
        return menu;
    }

    internal void SetPinnedToFront(WidgetConfig config, bool pinned, bool wholeGroup)
    {
        string? group = wholeGroup ? GetMagneticGroupId(config) : null;
        var targets = group is null ? MagneticSurfaceConfigs(config).ToArray() :
            _settingsService.Settings.Widgets.Where(w => GetMagneticGroupId(w) == group).ToArray();
        foreach (var member in targets) member.IsAlwaysOnTop = pinned;
        foreach (var window in GetLoadedDesktopWindows().OfType<WidgetWindowBase>().Where(w => targets.Any(t => t.Id == w.Config.Id)))
            window.RefreshFrontPinState();
        _settingsService.UpdateWidgetsBatch(targets, notifySubscribers: false);
        _settingsService.SaveDebounced(notifySubscribers: false);
    }
}
