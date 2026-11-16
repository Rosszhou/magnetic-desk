namespace MagneticDesk.Contracts;

public sealed record BackupSettingsSnapshot(bool LocalEnabled, int LocalIntervalMinutes, int LocalRetentionCount,
    string LocalDirectory, string EffectiveLocalDirectory, string LocalOpenDirectory, bool LocalDirectoryFallback);
public sealed record BackupSettingsChange(bool? LocalEnabled = null, int? LocalIntervalMinutes = null,
    int? LocalRetentionCount = null, string? LocalDirectory = null);
public interface IBackupSettings
{
    BackupSettingsSnapshot Read();
    void Update(BackupSettingsChange change);
    bool IsValidLocalDirectory(string path, out string? rejectionReasonKey);
    Task SaveAsync();
}
