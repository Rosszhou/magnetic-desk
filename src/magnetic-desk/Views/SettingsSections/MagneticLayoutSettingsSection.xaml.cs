using MagneticDesk.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Views.SettingsSections;

public sealed partial class MagneticLayoutSettingsSection : UserControl
{
    public MagneticLayoutSettingsSection() => InitializeComponent();

    public event EventHandler<SettingsSectionNavigationRequestedEventArgs>? NavigationRequested;

    private void Appearance_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new SettingsSectionNavigationRequestedEventArgs("AppearanceDensitySettings"));

    private void LayoutGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = e.NewSize.Width < 700;
        LayoutGrid.ColumnSpacing = stacked ? 0 : 20;
        LayoutGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
        Grid.SetColumn(PreviewColumn, stacked ? 0 : 1);
        Grid.SetRow(PreviewColumn, stacked ? 1 : 0);
    }
}
