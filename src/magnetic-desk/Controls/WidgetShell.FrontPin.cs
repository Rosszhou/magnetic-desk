using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MagneticDesk.Controls;

public sealed partial class WidgetShell
{
    internal event EventHandler<RoutedEventArgs>? FrontPinRequested;
    internal FrameworkElement FrontPinAnchor => _isCollapsed ? CapsuleFrontPinButton : TitleFrontPinButton;

    private void FrontPinButton_Click(object sender, RoutedEventArgs e) => FrontPinRequested?.Invoke(this, e);

    internal void SetPinnedToFront(bool pinned)
    {
        foreach (var button in new[] { CapsuleFrontPinButton, TitleFrontPinButton })
        {
            ToolTipService.SetToolTip(button, App.Current.LocalizationService.T(pinned ? "FrontPin.Pinned" : "FrontPin.Menu"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,
                App.Current.LocalizationService.T(pinned ? "FrontPin.Pinned" : "FrontPin.Menu"));
            if (pinned) button.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 151, 167));
            else button.ClearValue(Control.ForegroundProperty);
        }
    }
}
