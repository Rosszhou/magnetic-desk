#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.IO.Compression;
using System.Text.Json;
using MagneticDesk.Contracts;
using MagneticDesk.Features.Backup;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task RunFeaturePruningIntegrationAsync()
    {
        string path = Path.Combine(MagneticDeskDataPathService.Current.RootPath, "feature-pruning-integration.json");
        try
        {
            foreach (var kind in new[] { WidgetKind.Weather, WidgetKind.Music, WidgetKind.Glance })
            {
                Check(!WidgetRegistry.Default.CanCreateWindow(kind), "A removed widget can still be created");
                Check(!FeatureWidgetSettings.FeatureKinds.Contains(kind), "A removed widget still has a feature card");
            }
            var local = App.Current.DataBackupService;
            using var coordinator = new BackupSettingsCoordinator(_settingsService, local,
                () => local.UpdateAutomaticBackupOptions(DataBackupSettingsPolicy.GetOptions(_settingsService.Settings)));
            var editor = new BackupSettingsViewModel(coordinator, App.Current.LocalizationService.T);
            bool enabled = editor.LocalBackupEnabled;
            editor.LocalBackupEnabled = !enabled;
            Check(_settingsService.Settings.AutomaticBackupEnabled == !enabled, "The local backup editor cannot save its toggle");
            editor.LocalBackupEnabled = enabled;
            await _settingsService.FlushPendingSaveAsync(false);
            LocalBackupResult snapshot = await local.CreateAutomaticSnapshotResultAsync(true);
            Check(snapshot.Outcome == BackupOutcome.Succeeded && File.Exists(snapshot.ArchivePath), "Local snapshot creation failed");
            using (var archive = ZipFile.OpenRead(snapshot.ArchivePath!))
                Check(archive.Entries.Any(entry => entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)), "The snapshot has no manifest");
            App.Current.ShowSettings("BackupRestoreSettings");
            await Task.Delay(700);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, removedWidgetKinds = 3,
                localBackupEditor = true, localArchiveCreated = true, localSettingsOpened = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            App.Log($"[FeaturePruning] Integration failed: {ex}");
        }
        await RunRefinementIntegrationAsync();
    }
}
#endif
