using MagneticDesk.Contracts;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

/// <summary>
/// Sole settings-page writer for the feature section: the music presentation
/// options, the weather options (city, units, default view, skin, data
/// source, display toggles, refresh interval — normalized through the shared
/// <see cref="WeatherSettingsPolicy"/>), the feature-card enable states and
/// the section's misc presentation picks (attachment storage, managed-drop
/// action, folder-open behavior). Every edit keeps the original semantics:
/// normalize through the existing shared normalizers, compare against the raw
/// stored value, skip unchanged writes, store on the slice, and schedule one
/// debounced save; the boolean returns report whether the persisted value
/// changed. The reset ports write the fresh-install defaults without
/// scheduling a save because the feature-card reset flow performs one
/// explicit save after applying every feature's defaults. Feature enable and
/// disable reach the host only through the shell's existing
/// <see cref="FeatureWidgetSettings"/> write plus the WidgetManager sync
/// chain — no widget, runtime or broadcast behavior lives here.
/// </summary>
public sealed class FeatureWidgetsSettingsCoordinator : IFeatureWidgetsSettings
{
    private readonly SettingsService _settings;
    private bool _stopped;

    internal bool IsStopped => _stopped;

    public FeatureWidgetsSettingsCoordinator(SettingsService settings)
    {
        _settings = settings;
    }

    public void SetFeatureWidgetEnabled(WidgetKind kind, bool enabled)
    {
        ThrowIfStopped();
        // Write-only port: the caller's WidgetManager sync chain owns the
        // persistence for this path, exactly like the shell did.
        FeatureWidgetSettings.SetEnabled(_settings.Settings, kind, enabled);
    }

    public bool SetAttachmentStorageMode(string? mode)
    {
        ThrowIfStopped();
        string normalized = SettingsService.NormalizeAttachmentStorageMode(mode);
        QuickCaptureSettingsSlice quickCapture = _settings.Settings.QuickCapture;
        if (string.Equals(
                quickCapture.AttachmentStorageMode,
                normalized,
                StringComparison.Ordinal))
        {
            return false;
        }

        quickCapture.AttachmentStorageMode = normalized;
        _settings.SaveDebounced();
        return true;
    }

    public bool SetManagedDropAction(string? action)
    {
        ThrowIfStopped();
        string normalized = action switch
        {
            SettingsService.ManagedDropActionCopy =>
                SettingsService.ManagedDropActionCopy,
            SettingsService.ManagedDropActionFollowWindows =>
                SettingsService.ManagedDropActionFollowWindows,
            _ => SettingsService.ManagedDropActionMove
        };
        FileWidgetSettingsSlice fileWidget = _settings.Settings.FileWidget;
        if (string.Equals(
                fileWidget.ManagedDropAction,
                normalized,
                StringComparison.Ordinal))
        {
            return false;
        }

        fileWidget.ManagedDropAction = normalized;
        _settings.SaveDebounced();
        return true;
    }

    public string ReadFileWidgetFolderOpenBehavior() =>
        FileWidgetFolderOpenBehaviorNames.NormalizeGlobal(
            _settings.Settings.FileWidget.FileWidgetFolderOpenBehavior);

    public string ReadAttachmentStorageMode() =>
        Contracts.AttachmentStorageModes.Normalize(
            _settings.Settings.QuickCapture.AttachmentStorageMode);

    public bool SetFileWidgetFolderOpenBehavior(string? behavior)
    {
        ThrowIfStopped();
        string normalized =
            FileWidgetFolderOpenBehaviorNames.NormalizeGlobal(behavior);
        FileWidgetSettingsSlice fileWidget = _settings.Settings.FileWidget;
        if (string.Equals(
                fileWidget.FileWidgetFolderOpenBehavior,
                normalized,
                StringComparison.Ordinal))
        {
            return false;
        }

        fileWidget.FileWidgetFolderOpenBehavior = normalized;
        _settings.SaveDebounced();
        return true;
    }

    private void ThrowIfStopped() => ObjectDisposedException.ThrowIf(_stopped, this);

    internal void Stop() => _stopped = true;
}
