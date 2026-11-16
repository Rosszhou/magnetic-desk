using MagneticDesk.Controls;
using MagneticDesk.Controls.WidgetContents;

namespace MagneticDesk.Views;

public sealed partial class ContentWidgetWindow
{
    private WidgetCompactPresentation CreateQuickCaptureCompactPresentation(
        QuickCaptureWidgetContentAdapter quickCapture,
        string contentMode)
    {
        bool hidesSensitiveContent =
            SettingsService.Settings.WidgetCompactHideSensitiveContent;
        var latestItem = quickCapture.ViewModel.Items.FirstOrDefault();
        int count = quickCapture.ViewModel.IsClipboardWidget
            ? quickCapture.ViewModel.RecentCount : quickCapture.ViewModel.RecordCount;
        string countKey = quickCapture.ViewModel.IsClipboardWidget ? "Clipboard.Count" : "Widget.Compact.QuickCaptureCount";
        string summary = contentMode switch
        {
            MagneticDesk.Services.SettingsService.WidgetCompactContentModeMinimal => string.Empty,
            MagneticDesk.Services.SettingsService.WidgetCompactContentModeSmart
                when !hidesSensitiveContent =>
                latestItem?.DisplayText?.ReplaceLineEndings(" ").Trim() ??
                App.Current.LocalizationService.Format(
                    countKey,
                    count),
            _ => App.Current.LocalizationService.Format(
                countKey,
                count)
        };

        return new WidgetCompactPresentation(
            quickCapture.ViewModel.DisplayName,
            summary,
            quickCapture.ViewModel.IsClipboardWidget ? "\uE77F" : "\uE70F",
            App.Current.LocalizationService.T(
                "Widget.Compact.QuickCaptureDropHint"),
            UseStackedText:
                contentMode == MagneticDesk.Services.SettingsService.WidgetCompactContentModeSmart &&
                !hidesSensitiveContent,
            // Note bodies can be arbitrarily long; the capsule stays static
            // instead of marqueeing (same policy as Todo).
            LiveStateKey: string.Join(
                "|",
                quickCapture.ViewModel.RecordCount,
                summary));
    }
}
