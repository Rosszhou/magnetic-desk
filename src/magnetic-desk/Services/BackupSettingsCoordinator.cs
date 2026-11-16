using MagneticDesk.Contracts;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

/// <summary>Adapts the persisted backup slice and borrowed backup services to the settings editor.</summary>
public sealed class BackupSettingsCoordinator : IBackupSettings, IDisposable
{
    private readonly SettingsService _settings;
    private readonly MagneticDeskDataBackupService _local;
    private readonly Action _refreshRuntimeOptions;
    private bool _disposed;

    internal BackupSettingsCoordinator(SettingsService settings, MagneticDeskDataBackupService local,
        Action refreshRuntimeOptions)
    {
        _settings = settings;
        _local = local;
        _refreshRuntimeOptions = refreshRuntimeOptions;
    }


    public BackupSettingsSnapshot Read()
    {
        AppSettings settings = _settings.Settings;
        AutomaticBackupDirectoryStatus local = _local.GetAutomaticBackupDirectoryStatus();
        return new(
            settings.Backup.AutomaticBackupEnabled,
            DataBackupSettingsPolicy.NormalizeIntervalMinutes(settings.Backup.AutomaticBackupIntervalMinutes),
            DataBackupSettingsPolicy.NormalizeRetentionCount(settings.Backup.AutomaticBackupRetentionCount),
            DataBackupSettingsPolicy.NormalizeCustomDirectory(settings.Backup.AutomaticBackupDirectory) ?? string.Empty,
            local.EffectiveDirectory,
            local.IsCustomDirectoryActive ? local.EffectiveDirectory :
                Path.GetDirectoryName(_local.AutomaticSnapshotDirectory) ?? _local.AutomaticSnapshotDirectory,
            local.ConfiguredDirectory is not null &&
            (!local.IsCustomDirectoryActive || _local.LastAutomaticSnapshotFallbackMessage is not null));
    }

    public void Update(BackupSettingsChange change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AppSettings settings = _settings.Settings;
        bool changed = false;
        if (change.LocalEnabled is { } localEnabled && settings.Backup.AutomaticBackupEnabled != localEnabled)
            { settings.Backup.AutomaticBackupEnabled = localEnabled; changed = true; }
        if (change.LocalIntervalMinutes is { } localInterval)
        {
            int value = DataBackupSettingsPolicy.NormalizeIntervalMinutes(localInterval);
            if (settings.Backup.AutomaticBackupIntervalMinutes != value)
                { settings.Backup.AutomaticBackupIntervalMinutes = value; changed = true; }
        }
        if (change.LocalRetentionCount is { } localRetention)
        {
            int value = DataBackupSettingsPolicy.NormalizeRetentionCount(localRetention);
            if (settings.Backup.AutomaticBackupRetentionCount != value)
                { settings.Backup.AutomaticBackupRetentionCount = value; changed = true; }
        }
        if (change.LocalDirectory is { } localDirectory)
        {
            string value = DataBackupSettingsPolicy.NormalizeCustomDirectory(localDirectory) ?? string.Empty;
            if (settings.Backup.AutomaticBackupDirectory != value)
                { settings.Backup.AutomaticBackupDirectory = value; changed = true; }
        }
        if (!changed) return;
        _settings.SaveDebounced();
        _refreshRuntimeOptions();
    }

    public bool IsValidLocalDirectory(string path, out string? rejectionReasonKey) =>
        _local.IsValidCustomAutomaticBackupDirectory(path, out rejectionReasonKey);

    public Task SaveAsync() => _settings.SaveAsync();

    public void Dispose() => _disposed = true;
}
