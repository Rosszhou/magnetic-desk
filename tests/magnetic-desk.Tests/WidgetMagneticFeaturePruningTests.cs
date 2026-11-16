using System.Text.Json;
using MagneticDesk.Contracts;
using MagneticDesk.Features.Backup;
using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticFeaturePruningTests
{
    [Theory]
    [InlineData(WidgetKind.Weather)]
    [InlineData(WidgetKind.Music)]
    [InlineData(WidgetKind.Glance)]
    public void LegacyWidgets_AreReadableAndCannotBeActivated(WidgetKind kind)
    {
        var widget = new WidgetConfig { WidgetKind = kind };
        var settings = new AppSettings();
        settings.FeatureWidgetEnabledStates[kind.ToString()] = true;
        Assert.True(WidgetRegistry.Default.IsKnown(kind));
        Assert.False(WidgetRegistry.Default.IsAvailableForSession(widget, settings));
        Assert.DoesNotContain(kind, FeatureWidgetSettings.FeatureKinds);
        Assert.DoesNotContain(WidgetContentFactory.DescriptorList, descriptor => descriptor.WidgetKind == kind);
        Assert.Equal(kind, JsonSerializer.Deserialize<WidgetConfig>(JsonSerializer.Serialize(widget))!.WidgetKind);
    }

    [Fact]
    public void RemovedNetworkAndMediaImplementations_AreAbsent()
    {
        var assembly = typeof(WidgetConfig).Assembly;
        foreach (string name in new[] { "CloudBackupService", "WebDavBackupTransport", "WeatherService", "MusicSessionService", "GlanceWidgetStore" })
            Assert.Null(assembly.GetType("MagneticDesk.Services." + name));
    }

    [Fact]
    public void LocalBackupEditor_ProjectsAndPersistsLocalOptions()
    {
        var settings = new LocalSettings();
        var editor = new BackupSettingsViewModel(settings);
        Assert.True(editor.LocalBackupEnabled);
        Assert.Equal(720, editor.SelectedLocalIntervalMinutes);
        editor.LocalBackupEnabled = false;
        editor.SelectedLocalIntervalMinutes = 1440;
        editor.SelectedLocalRetentionCount = 14;
        Assert.False(settings.State.LocalEnabled);
        Assert.Equal(1440, settings.State.LocalIntervalMinutes);
        Assert.Equal(14, settings.State.LocalRetentionCount);
        Assert.NotEmpty(editor.AvailableLocalIntervalOptions);
        Assert.NotEmpty(editor.AvailableLocalRetentionOptions);
    }

    private sealed class LocalSettings : IBackupSettings
    {
        public BackupSettingsSnapshot State { get; private set; } = new(true, 720, 10, "", "snapshots", "snapshots", false);
        public BackupSettingsSnapshot Read() => State;
        public void Update(BackupSettingsChange change) => State = State with
        {
            LocalEnabled = change.LocalEnabled ?? State.LocalEnabled,
            LocalIntervalMinutes = change.LocalIntervalMinutes ?? State.LocalIntervalMinutes,
            LocalRetentionCount = change.LocalRetentionCount ?? State.LocalRetentionCount,
            LocalDirectory = change.LocalDirectory ?? State.LocalDirectory
        };
        public bool IsValidLocalDirectory(string path, out string? rejectionReasonKey) { rejectionReasonKey = null; return true; }
        public Task SaveAsync() => Task.CompletedTask;
    }
}
