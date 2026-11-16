using CommunityToolkit.Mvvm.ComponentModel;
using MagneticDesk.Contracts;
namespace MagneticDesk.Features.Backup;

public sealed partial class BackupSettingsViewModel : ObservableObject
{
    private readonly IBackupSettings _settings;
    private readonly Func<string, string> _localize;
    private readonly Func<string, object[], string> _format;
    public BackupSettingsSnapshot State { get; private set; }
    public BackupSettingsViewModel(IBackupSettings settings,
        Func<string, string>? localize = null, Func<string, object[], string>? format = null)
    {
        _settings = settings;
        _localize = localize ?? (key => key);
        _format = format ?? ((key, args) => key);
        State = settings.Read();
        RefreshState();
    }
    private string Localize(string key) => _localize(key);
    private string Format(string key, params object[] args) => _format(key, args);
    private void Update(BackupSettingsChange change) { _settings.Update(change); RefreshState(); }
    public bool IsValidLocalDirectory(string path, out string? reason) => _settings.IsValidLocalDirectory(path, out reason);
    public void RefreshState()
    {
        State = _settings.Read();
        _isSyncingPresentation = true;
        try
        {
            LocalBackupEnabled = State.LocalEnabled;
            SelectedLocalIntervalMinutes = State.LocalIntervalMinutes;
            SelectedLocalRetentionCount = State.LocalRetentionCount;
            _localDirectory = State.LocalDirectory;
            _localFallbackWarningText = State.LocalDirectoryFallback
                ? Format("Settings.DataBackup.AutomaticBackupDirectory.FallbackWarning", State.EffectiveLocalDirectory) : string.Empty;
        }
        finally { _isSyncingPresentation = false; }
        OnPropertyChanged(string.Empty);
    }
    public void RefreshLocalization()
    {
        _cachedLocalIntervalNames = null;
        _cachedLocalRetentionNames = null;
        OnPropertyChanged(string.Empty);
    }
}
