using MagneticDesk.Models;
using MagneticDesk.Services;
using Microsoft.UI.Xaml;

namespace MagneticDesk.Helpers;

public static class QuickCaptureClipboardActivationHelper
{
    public static async Task<bool> EnableAsync(XamlRoot? xamlRoot, LocalizationService localizationService)
    {
        await App.Current.QuickCaptureSettings.EnableClipboardFromOpenWidgetAsync();
        App.Log("[QuickCaptureClipboard] Enabled");
        return true;
    }
}
