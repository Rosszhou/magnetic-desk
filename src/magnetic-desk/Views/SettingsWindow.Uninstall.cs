using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Views;

public sealed partial class SettingsWindow
{
    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        string root = AppContext.BaseDirectory;
        string installerUninstall = System.IO.Path.Combine(root, "unins000.exe");
        string portableUninstall = System.IO.Path.Combine(root, "magnetic-desk.Uninstaller.exe");
        string? executable = System.IO.File.Exists(installerUninstall) ? installerUninstall :
            System.IO.File.Exists(portableUninstall) ? portableUninstall : null;
        try
        {
            if (executable is null) throw new InvalidOperationException(_localizationService.IsEnglish
                ? "The development build has no uninstaller. Use the published application's uninstall entry."
                : "开发构建没有卸载器，请从已发布的软件目录或 Windows 已安装的应用中卸载。");
            _ = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = root })
                ?? throw new InvalidOperationException("Unable to start the uninstaller.");
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                XamlRoot = SettingsRoot.XamlRoot,
                RequestedTheme = SettingsRoot.ActualTheme,
                Title = _localizationService.IsEnglish ? "Uninstall" : "卸载软件",
                Content = ex.Message,
                CloseButtonText = _localizationService.T("Common.Close")
            }.ShowAsync();
        }
    }
}
