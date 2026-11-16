using MagneticDesk.Models;
using MagneticDesk.Views;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private readonly Dictionary<string, CapsuleAppearanceWindow> _capsuleAppearanceWindows = [];

    private void AppendCapsuleAppearanceMenu(MenuFlyout flyout, WidgetConfig config)
    {
        var localization = _localizationService;
        var menu = new MenuFlyoutSubItem { Text = localization.T(GetMagneticGroupId(config) is null ? "Capsule.Style.Menu" : "Capsule.Style.GroupMenu") };
        var preview = new MenuFlyoutItem { Text = localization.T("Capsule.Gallery.Open") };
        preview.Click += (_, _) => ShowCapsuleAppearance(config);
        menu.Items.Add(preview);
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var style in Enum.GetValues<WidgetCapsuleStyle>())
        {
            var item = new ToggleMenuFlyoutItem { Text = localization.T($"Capsule.Style.{style}"),
                IsChecked = WidgetCapsuleAppearance.Normalize(config.CapsuleStyle) == style };
            item.Click += (_, _) => SetCapsuleAppearance(config, style);
            menu.Items.Add(item);
        }
        flyout.Items.Add(menu);
    }

    internal void ShowCapsuleAppearance(WidgetConfig config)
    {
        if (_capsuleAppearanceWindows.TryGetValue(config.Id, out var existing))
        {
            existing.Activate();
            return;
        }
        var window = new CapsuleAppearanceWindow(config, GetMagneticGroupId(config) is not null,
            (style, wholeGroup) => SetCapsuleAppearance(config, style, wholeGroup));
        _capsuleAppearanceWindows.Add(config.Id, window);
        window.Closed += (_, _) => _capsuleAppearanceWindows.Remove(config.Id);
        window.Activate();
    }

    internal void SetCapsuleAppearance(WidgetConfig config, WidgetCapsuleStyle style, bool wholeGroup = true)
    {
        if (!_settingsService.Settings.WidgetLayout.Widgets.Any(w => w.Id == config.Id)) return;
        string? group = wholeGroup ? GetMagneticGroupId(config) : null;
        var targets = group is null ? MagneticSurfaceConfigs(config).ToArray() :
            _settingsService.Settings.WidgetLayout.Widgets.Where(w => GetMagneticGroupId(w) == group).ToArray();
        foreach (var target in targets) target.CapsuleStyle = style.ToString();
        foreach (var window in GetLoadedDesktopWindows().OfType<WidgetWindowBase>().Where(w => targets.Any(t => t.Id == w.Config.Id)))
            window.RefreshCapsuleAppearance();
        _settingsService.UpdateWidgetsBatch(targets, notifySubscribers: false);
        _settingsService.SaveDebounced(notifySubscribers: false);
    }
}
