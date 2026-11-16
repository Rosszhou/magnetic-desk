using MagneticDesk.Models;

namespace MagneticDesk.Services;

internal sealed class DisabledAppUpdateService : IAppUpdateService
{
    private const string DisabledMessage = "Application updates are disabled in this edition.";
    public AppUpdateCheckResult? LastCheckResult => null;
    public DateTime? LastCheckTimeUtc => null;
    public string ManifestUrl => string.Empty;
    public AppUpdateDeliveryKind DeliveryKind => AppUpdateDeliveryKind.DirectInstaller;
    public string? LastDownloadedInstallerPath => null;
    public bool IsDownloadInProgress => false;
    public event Action<AppUpdateCheckResult>? CheckCompleted { add { } remove { } }
    public event Action<AppUpdateDownloadProgress>? DownloadProgressChanged { add { } remove { } }
    public event Action<AppUpdateDownloadResult>? DownloadCompleted { add { } remove { } }
    public void ClearLastDownloadedInstaller() { }
    public Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
        CheckForUpdatesAsync(AppUpdateService.GetCurrentAppVersion(), cancellationToken);
    public Task<AppUpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppUpdateCheckResult(AppUpdateCheckStatus.Failed, currentVersion, errorMessage: DisabledMessage));
    }
    public Task<AppUpdateDownloadResult> DownloadUpdateAsync(AppUpdateManifest manifest,
        IProgress<AppUpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AppUpdateDownloadResult.Failed(AppUpdateDownloadFailureKind.InvalidManifest, DisabledMessage));
    }
    public AppUpdateInstallResult StartInstallerHelper(string installerPath, bool silent = true, string? appCultureName = null) =>
        AppUpdateInstallResult.Failed(DisabledMessage);
}
