using MagneticDesk.Contracts;

namespace MagneticDesk.Services;

internal sealed class BackupBackend(
    SettingsService settings,
    MagneticDeskDataBackupService local) : IBackupBackend
{
    public bool LocalScheduleEnabled => local.AutomaticBackupOptions.IsEnabled;

    public bool RefreshOptions()
    {
        // Record value equality covers every effective field. LastSuccessUtc
        // is excluded on the cloud side: the service updates it in memory on
        // every successful upload, and a failed settings persist would then
        // make every refresh look "changed" forever.
        AutomaticBackupOptions previousLocal = local.AutomaticBackupOptions;
        local.UpdateAutomaticBackupOptions(DataBackupSettingsPolicy.GetOptions(settings.Settings));
        return previousLocal != local.AutomaticBackupOptions;
    }

    public async Task PrepareManualBackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await settings.FlushPendingSaveAsync(notifySubscribers: false))
            throw new IOException("Settings could not be saved before creating a backup.");
        cancellationToken.ThrowIfCancellationRequested();
        RefreshOptions();
    }

    public Task<LocalBackupResult> CreateSnapshotAsync(bool force, CancellationToken cancellationToken) =>
        local.CreateAutomaticSnapshotResultAsync(force, cancellationToken);

    public Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken) =>
        local.ExportBackupAsync(destinationDirectory, cancellationToken);

}
