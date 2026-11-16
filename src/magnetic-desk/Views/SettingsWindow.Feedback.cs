using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Views;

public sealed partial class SettingsWindow
{
    private bool _feedbackDialogShowing;

    private async void ShowFeedbackDialogButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsRoot.XamlRoot is null || _feedbackDialogShowing) return;
        _feedbackDialogShowing = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = SettingsRoot.XamlRoot,
                RequestedTheme = SettingsRoot.ActualTheme,
                Title = _localizationService.T("Feedback.Dialog.Title"),
                Content = new TextBlock
                {
                    Text = "cheeseburger2026@qq.com",
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = _localizationService.T("Common.Close")
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _feedbackDialogShowing = false;
        }
    }
}
