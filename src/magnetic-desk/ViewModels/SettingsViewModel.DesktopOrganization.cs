using MagneticDesk.Services;

namespace MagneticDesk.ViewModels;

public sealed partial class SettingsViewModel
{
    public void RefreshManagedStorageState()
    {
        ManagedStorageRootPath = SettingsService.NormalizeManagedStorageRootPath(
            _settingsService.Settings.DefaultManagedStorageRootPath);
    }
}
