namespace MagneticDesk.Contracts;

public enum BackupOutcome { Succeeded, Skipped, AlreadyRunning, Canceled, Failed }
public enum BackupWorkKind { Local }
public sealed record LocalBackupResult(BackupOutcome Outcome, string? ArchivePath = null, string? Error = null);
public sealed record BackupCompletion(BackupWorkKind Kind, bool Scheduled, BackupOutcome Outcome, string? Error = null);

public interface IBackupCommands
{
    bool IsStopping { get; }
    Task<LocalBackupResult> CreateSnapshotNowAsync(CancellationToken cancellationToken = default);
    Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken = default);
}

public interface IBackupBackend
{
    bool LocalScheduleEnabled { get; }

    /// <summary>
    /// Re-reads effective options from settings and pushes them to the local
    /// and cloud services. Returns whether the effective scheduling options
    /// actually changed, so callers can skip redundant schedule evaluations.
    /// </summary>
    bool RefreshOptions();
    Task PrepareManualBackupAsync(CancellationToken cancellationToken);
    Task<LocalBackupResult> CreateSnapshotAsync(bool force, CancellationToken cancellationToken);
    Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken);
}

/// <summary>UI-thread-owned timer. Only the backup runtime starts and stops it.</summary>
public interface IBackupTimer : IDisposable
{
    event Action? Tick;
    void Start();
    void Stop();
}
