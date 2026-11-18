using MagneticDesk.Services;
using Microsoft.UI.Xaml;

namespace MagneticDesk.Views;

public sealed partial class OnboardingWindow
{
    private async void CreateWidget_Click(object sender, RoutedEventArgs e)
    {
        if (!_createWidgetTask.IsCompleted || _completing) return;
        CreateWidgetButton.IsEnabled = false;
        _createWidgetTask = CreateWidgetForIntroAsync();
        await _createWidgetTask;
        if (!_isClosed && !_completing) CreateWidgetButton.IsEnabled = true;
    }

    private async Task CreateWidgetForIntroAsync()
    {
        try
        {
            if (App.Current.WidgetManager is not { } manager) return;
            await manager.CreateManagedWidgetAsync();
            if (!_isClosed) CreateStatusText.Text = _localizationService.T("Design.Intro.Created");
        }
        catch (Exception ex)
        {
            App.Log($"[Onboarding] Could not create widget: {ex}");
            if (!_isClosed) CreateStatusText.Text = _localizationService.T("Design.Intro.CreateFailed");
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings("MagneticLayout");

    private void RefreshShortcut()
    {
        var settings = _settingsService.Settings.Core;
        SummonShortcutText.Text = GlobalHotkeyService.FormatActivation(
            GlobalHotkeyService.NormalizeActivation(settings.GlobalHotkeyActivationKind,
                settings.GlobalHotkeyModifiers, settings.GlobalHotkeyKey), _localizationService);
    }
}
