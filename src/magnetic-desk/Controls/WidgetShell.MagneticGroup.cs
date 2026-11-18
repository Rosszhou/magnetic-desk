using MagneticDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Controls;

public sealed partial class WidgetShell
{
    internal event EventHandler<RoutedEventArgs>? MagneticGroupRequested;
    internal FrameworkElement MagneticGroupAnchor => MagneticGroupButton;

    private void MagneticGroupButton_Click(object sender, RoutedEventArgs e) => MagneticGroupRequested?.Invoke(this, e);

    internal void SetMagneticGroupState(bool grouped, bool locked)
    {
        MagneticGroupButton.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        MagneticGroupGlyph.Glyph = locked ? "\uE72E" : "\uE71B";
        string title = App.Current.LocalizationService.T(locked ? "Design.Group.Locked" : "Design.Group.Status");
        ToolTipService.SetToolTip(MagneticGroupButton, title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MagneticGroupButton, title);
    }
}
