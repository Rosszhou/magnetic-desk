using MagneticDesk.Models;
using MagneticDesk.Views;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private readonly SemaphoreSlim _clipboardWidgetGate = new(1, 1);

    public async Task<ContentWidgetWindow> CreateOrShowClipboardWidgetAsync()
    {
        await _clipboardWidgetGate.WaitAsync();
        try
        {
            var config = _settingsService.Settings.WidgetLayout.Widgets.FirstOrDefault(w =>
                w.WidgetKind == WidgetKind.Clipboard && !IsDeleted(w.Id));
            if (config is null)
            {
                config = new WidgetConfig
                {
                    Name = _localizationService.T("Clipboard.Title"),
                    WidgetKind = WidgetKind.Clipboard,
                    BoundsCoordinateVersion = WidgetConfig.CurrentBoundsCoordinateVersion,
                    Width = _settingsService.Settings.WidgetShell.DefaultWidgetWidth,
                    Height = _settingsService.Settings.WidgetShell.DefaultWidgetHeight
                };
                ApplyNewWidgetPlacement(config);
                _settingsService.Settings.WidgetLayout.Widgets.Add(config);
            }
            config.IsDisabled = false;
            config.IsVisible = true;
            await _settingsService.SaveAsync();
            var window = await CreateContentWidgetFromConfigAsync(config);
            window.RevealFromTray(autoRestore: false);
            return window;
        }
        finally
        {
            _clipboardWidgetGate.Release();
        }
    }

    public async Task SetClipboardWidgetEnabledAsync(string id, bool enabled)
    {
        await _clipboardWidgetGate.WaitAsync();
        try
        {
            var config = _settingsService.Settings.WidgetLayout.Widgets.FirstOrDefault(w =>
                w.Id == id && w.WidgetKind == WidgetKind.Clipboard);
            if (config is null) return;
            config.IsDisabled = !enabled;
            config.IsVisible = enabled;
            await _settingsService.SaveAsync();
            if (enabled)
            {
                await ShowWidgetAsync(id, reveal: true, autoRestoreOnReveal: false);
            }
            else if (_contentWidgets.TryGetValue(id, out var window))
            {
                window.HideWindow();
            }
        }
        finally
        {
            _clipboardWidgetGate.Release();
        }
    }
}
