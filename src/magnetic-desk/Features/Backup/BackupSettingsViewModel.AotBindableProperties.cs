#if MAGNETIC_DESK_NATIVE_AOT
namespace MagneticDesk.Features.Backup;

// The backup settings family (local backups, cloud backups and the
// compatibility-diagnostics section, batch 49) keeps its runtime {Binding}
// surface and binds through section-level DataContext switches. Expose only
// the properties used by that XAML surface in NativeAOT builds, mirroring
// the Glance/Music/FileStack/QuickCapture/Todo/Weather editor bridge
// pattern. RemoteSnapshotItems is intentionally absent: the snapshot
// ListView's ItemsSource is assigned in code-behind (an object[] snapshot),
// so no {Binding} ever reads the collection property.
[WinRT.GeneratedBindableCustomProperty([
    nameof(AvailableLocalIntervalOptions),
    nameof(AvailableLocalRetentionOptions),
    nameof(CanRepairDragDrop),
    nameof(CanResyncRuntime),
    nameof(CustomDirectoryRecoveryNoteText),
    nameof(DragDropAppCompatText),
    nameof(DragDropDetailText),
    nameof(DragDropExplorerText),
    nameof(DragDropProcessText),
    nameof(DragDropRepairStatusText),
    nameof(DragDropShortcutText),
    nameof(DragDropStartupText),
    nameof(DragDropSummaryText),
    nameof(DragDropUacText),
    nameof(LocalBackupEnabled),
    nameof(LocalDirectoryDisplayText),
    nameof(LocalFallbackWarningText),
    nameof(RuntimeDetailText),
    nameof(RuntimeSummaryText),
    nameof(SelectedLocalIntervalMinutes),
    nameof(SelectedLocalRetentionCount),
    nameof(ShowCustomDirectoryRecoveryNote),
    nameof(ShowLocalFallbackWarning),
], [])]
public sealed partial class BackupSettingsViewModel
{
}
#endif
