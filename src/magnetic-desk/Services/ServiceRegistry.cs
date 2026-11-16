using Microsoft.Extensions.DependencyInjection;

namespace MagneticDesk.Services;

/// <summary>
/// Central DI registration for all core MagneticDesk services.
/// These registrations have application lifetime. Feature/window factories
/// own shorter-lived instances; App disposes this container after its consumers.
/// </summary>
public static class ServiceRegistry
{
    /// <summary>
    /// Registers all core application services into the given service collection.
    /// </summary>
    public static IServiceCollection AddMagneticDeskServices(this IServiceCollection services)
    {
        // ── Core infrastructure ──────────────────────────────────────────
        services.AddSingleton<SettingsService>();
        services.AddSingleton<MagneticDeskDataBackupService>();
        services.AddSingleton<MagneticDeskAttachmentHealthService>();
        services.AddSingleton<MagneticDeskDiagnosticsBundleService>();
        services.AddSingleton<FileService>();
        services.AddSingleton<ResizeGuideOverlayService>();
        services.AddSingleton<ManagedStorageDesktopShortcutService>();

        // ── Feature services ─────────────────────────────────────────────
        services.AddSingleton<OrganizerService>(sp =>
            new OrganizerService(
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<FileService>()));
        services.AddSingleton<QuickCaptureService>(sp => new QuickCaptureService(
            recycleAttachmentFileAsync: path =>
                sp.GetRequiredService<FileService>().DeleteEntryAsync(path, recycle: true)));
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ThemeService>();

        // ── Update (factory-based) ───────────────────────────────────────
        services.AddSingleton<IAppUpdateService>(_ =>
            AppUpdateServiceFactory.Create(AppDistributionService.Current));

        return services;
    }
}
