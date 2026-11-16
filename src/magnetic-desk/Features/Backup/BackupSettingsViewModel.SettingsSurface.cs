using MagneticDesk.Contracts;
using MagneticDesk.Models;
namespace MagneticDesk.Features.Backup;
public sealed partial class BackupSettingsViewModel
{
    private bool _isSyncingPresentation;

    private string _localDirectory = string.Empty;

    private string _localFallbackWarningText = string.Empty;

    private bool _localBackupEnabled = BackupOptionKinds.DefaultLocalEnabled;

    private int _selectedLocalIntervalMinutes = BackupOptionKinds.DefaultLocalIntervalMinutes;

    private int _selectedLocalRetentionCount = BackupOptionKinds.DefaultLocalRetentionCount;

    private string _dragDropSummaryText = string.Empty;

    private string _dragDropDetailText = string.Empty;

    private string _dragDropProcessText = string.Empty;

    private string _dragDropExplorerText = string.Empty;

    private string _dragDropUacText = string.Empty;

    private string _dragDropAppCompatText = string.Empty;

    private string _dragDropStartupText = string.Empty;

    private string _dragDropShortcutText = string.Empty;

    private string _dragDropRepairStatusText = string.Empty;

    private bool _isDragDropRepairing;

    private bool _canRepairDragDrop;

    private string _runtimeSummaryText = string.Empty;

    private string _runtimeDetailText = string.Empty;

    private bool _isRuntimeResyncing;

    private string[]? _cachedLocalIntervalNames;

    private string[]? _cachedLocalRetentionNames;


    // --- Local automatic snapshots ---

    public bool LocalBackupEnabled
    {
        get => _localBackupEnabled;
        set
        {
            if (!SetProperty(ref _localBackupEnabled, value))
            {
                return;
            }

            if (_isSyncingPresentation)
            {
                return;
            }

            Update(new(LocalEnabled: value));
        }
    }


    public int SelectedLocalIntervalMinutes
    {
        get => _selectedLocalIntervalMinutes;
        set
        {
            int normalized = BackupOptionKinds.NormalizeLocalIntervalMinutes(value);
            if (!SetProperty(ref _selectedLocalIntervalMinutes, normalized))
            {
                return;
            }

            if (_isSyncingPresentation)
            {
                return;
            }

            Update(new(LocalIntervalMinutes: normalized));
        }
    }


    public IReadOnlyList<SettingsOption> AvailableLocalIntervalOptions
    {
        get
        {
            _cachedLocalIntervalNames ??= BackupOptionKinds.LocalIntervalMinutes
                .Select(GetLocalIntervalDisplayName)
                .ToArray();
            return WrapOptions(BuildOptions(BackupOptionKinds.LocalIntervalMinutes, _cachedLocalIntervalNames));
        }
    }


    public int SelectedLocalRetentionCount
    {
        get => _selectedLocalRetentionCount;
        set
        {
            int normalized = BackupOptionKinds.NormalizeLocalRetentionCount(value);
            if (!SetProperty(ref _selectedLocalRetentionCount, normalized))
            {
                return;
            }

            if (_isSyncingPresentation)
            {
                return;
            }

            Update(new(LocalRetentionCount: normalized));
        }
    }


    public IReadOnlyList<SettingsOption> AvailableLocalRetentionOptions
    {
        get
        {
            _cachedLocalRetentionNames ??= BackupOptionKinds.LocalRetentionCounts
                .Select(count => Format("Settings.DataBackup.Retention.Count", count))
                .ToArray();
            return WrapOptions(BuildOptions(BackupOptionKinds.LocalRetentionCounts, _cachedLocalRetentionNames));
        }
    }


    /// <summary>Folder shown in the UI: the configured folder, or the effective default one.</summary>
    public string LocalDirectoryDisplayText =>
        _localDirectory.Length > 0 ? _localDirectory : State.EffectiveLocalDirectory;


    public string LocalFallbackWarningText => _localFallbackWarningText;


    public bool ShowLocalFallbackWarning => !string.IsNullOrEmpty(_localFallbackWarningText);


    /// <summary>
    /// Disclosure for the wipe-recovery limitation: a custom snapshot folder
    /// survives a configuration wipe, but the folder is only recorded inside
    /// settings.json — after a wipe the automatic recovery offer cannot see
    /// those snapshots, so restoring from them stays a manual step.
    /// </summary>
    public bool ShowCustomDirectoryRecoveryNote => _localDirectory.Length > 0;


    public string CustomDirectoryRecoveryNoteText =>
        Format("Settings.DataBackup.AutomaticBackupDirectory.CustomDirectoryRecoveryNote");


    /// <summary>
    /// Writes a custom snapshot folder chosen in the folder picker; empty
    /// resets to the default recovery folder.
    /// </summary>
    public void UpdateLocalDirectory(string path)
    {
        string normalized = BackupOptionKinds.NormalizeLocalDirectory(path) ?? string.Empty;
        Update(new(LocalDirectory: normalized));
    }


    /// <summary>
    /// Re-reads the effective snapshot folder and refreshes the fallback
    /// warning; called when the local-backup section is entered.
    /// </summary>
    public void RefreshLocalStatus() => RefreshState();


    // --- Compatibility diagnostics (computed on the shell, pushed in) ---

    /// <summary>
    /// The drag-drop permission diagnostic projection pushed in by the shell
    /// (the diagnose/repair runs against host services there).
    /// </summary>
    public sealed record DragDropDiagnosticsView(
        string SummaryText,
        string DetailText,
        string ProcessText,
        string ExplorerText,
        string UacText,
        string AppCompatText,
        string StartupText,
        string ShortcutText,
        bool CanRepair);


    public string DragDropSummaryText => _dragDropSummaryText;

    public string DragDropDetailText => _dragDropDetailText;

    public string DragDropProcessText => _dragDropProcessText;

    public string DragDropExplorerText => _dragDropExplorerText;

    public string DragDropUacText => _dragDropUacText;

    public string DragDropAppCompatText => _dragDropAppCompatText;

    public string DragDropStartupText => _dragDropStartupText;

    public string DragDropShortcutText => _dragDropShortcutText;


    public string DragDropRepairStatusText
    {
        get => _dragDropRepairStatusText;
        private set => SetProperty(ref _dragDropRepairStatusText, value);
    }


    public bool CanRepairDragDrop => _canRepairDragDrop && !_isDragDropRepairing;


    public void SetDragDropDiagnostic(DragDropDiagnosticsView view)
    {
        _dragDropSummaryText = view.SummaryText;
        _dragDropDetailText = view.DetailText;
        _dragDropProcessText = view.ProcessText;
        _dragDropExplorerText = view.ExplorerText;
        _dragDropUacText = view.UacText;
        _dragDropAppCompatText = view.AppCompatText;
        _dragDropStartupText = view.StartupText;
        _dragDropShortcutText = view.ShortcutText;
        _canRepairDragDrop = view.CanRepair;
        OnPropertyChanged(nameof(DragDropSummaryText));
        OnPropertyChanged(nameof(DragDropDetailText));
        OnPropertyChanged(nameof(DragDropProcessText));
        OnPropertyChanged(nameof(DragDropExplorerText));
        OnPropertyChanged(nameof(DragDropUacText));
        OnPropertyChanged(nameof(DragDropAppCompatText));
        OnPropertyChanged(nameof(DragDropStartupText));
        OnPropertyChanged(nameof(DragDropShortcutText));
        OnPropertyChanged(nameof(CanRepairDragDrop));
    }


    /// <summary>The shell pushes the busy gate while a repair round-trip runs.</summary>
    public void SetDragDropRepairBusy(bool busy)
    {
        if (_isDragDropRepairing == busy)
        {
            return;
        }

        _isDragDropRepairing = busy;
        OnPropertyChanged(nameof(CanRepairDragDrop));
    }


    public void SetDragDropRepairStatus(string status) => DragDropRepairStatusText = status;


    public string RuntimeSummaryText => _runtimeSummaryText;

    public string RuntimeDetailText => _runtimeDetailText;


    public bool CanResyncRuntime => !_isRuntimeResyncing;


    public void SetRuntimeHealth(string summaryText, string detailText)
    {
        _runtimeSummaryText = summaryText;
        _runtimeDetailText = detailText;
        OnPropertyChanged(nameof(RuntimeSummaryText));
        OnPropertyChanged(nameof(RuntimeDetailText));
    }


    /// <summary>The shell pushes the busy gate while a runtime resync runs.</summary>
    public void SetRuntimeResyncBusy(bool busy)
    {
        if (_isRuntimeResyncing == busy)
        {
            return;
        }

        _isRuntimeResyncing = busy;
        OnPropertyChanged(nameof(CanResyncRuntime));
    }


    private string GetLocalIntervalDisplayName(int minutes) => minutes switch
    {
        5 or 30 => Format("Settings.DataBackup.Interval.Minutes", minutes),
        60 => Localize("Settings.DataBackup.Interval.Hour"),
        720 => Format("Settings.DataBackup.Interval.Hours", 12),
        1440 => Localize("Settings.DataBackup.Interval.Day"),
        7200 => Format("Settings.DataBackup.Interval.Days", 5),
        _ => minutes.ToString()
    };


    // Build real SettingsOption[] arrays (not collection expressions): the
    // hidden read-only-array type cannot marshal across the WinRT ABI in
    // Native AOT builds and would leave the ItemsSource empty.
    private static SettingsOption[] BuildOptions<T>(T[] values, string[] displayNames)
    {
        var options = new SettingsOption[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            options[index] = new SettingsOption(values[index]!, displayNames[index]);
        }

        return options;
    }


    private static IReadOnlyList<SettingsOption> WrapOptions(SettingsOption[] options) =>
        options;

}
