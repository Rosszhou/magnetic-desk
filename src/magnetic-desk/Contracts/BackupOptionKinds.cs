namespace MagneticDesk.Contracts;

/// <summary>
/// Canonical option values, defaults and read normalization for the backup
/// settings family (local snapshots and cloud/WebDAV backups). The Services
/// policies (<c>DataBackupSettingsPolicy</c>/<c>CloudBackupSettingsPolicy</c>)
/// alias these members so the coordinator write path and the section editor
/// (batch 49, <c>Features/Backup/BackupSettingsViewModel</c>) share one
/// source of truth without the editor referencing Services.
/// </summary>
public static class BackupOptionKinds
{
    // --- Local automatic snapshots ---

    public const bool DefaultLocalEnabled = true;
    public const int DefaultLocalIntervalMinutes = 24 * 60;
    public const int DefaultLocalRetentionCount = 7;

    /// <summary>Interval presets offered in settings, in ascending order.</summary>
    public static readonly int[] LocalIntervalMinutes =
        [5, 30, 60, 12 * 60, 24 * 60, 5 * 24 * 60];

    /// <summary>Retention presets offered in settings, in ascending order.</summary>
    public static readonly int[] LocalRetentionCounts = [3, 5, 7, 14, 30];

    public static int NormalizeLocalIntervalMinutes(int value) =>
        LocalIntervalMinutes.Contains(value) ? value : DefaultLocalIntervalMinutes;

    public static int NormalizeLocalRetentionCount(int value) =>
        LocalRetentionCounts.Contains(value) ? value : DefaultLocalRetentionCount;

    public static string? NormalizeLocalDirectory(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    // Defaults retained for deserializing earlier settings files.
    public const string ProviderNone = "none";
    public const int DefaultCloudIntervalMinutes = 1440;
    public const int DefaultCloudRetentionCount = 5;
}
