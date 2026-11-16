using MagneticDesk.Contracts;
using MagneticDesk.Features.FeatureWidgets;
using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class FeatureWidgetsSettingsCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "magnetic-desk.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MiscWrites_NormalizeAndSkipUnchangedSaves()
    {
        var settings = new SettingsService(_root);
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        var editor = new FeatureWidgetsSettingsViewModel(coordinator, key => key);
        int notified = 0;
        settings.SettingsChanged += () => notified++;

        Assert.True(editor.SetAttachmentStorageMode(SettingsService.AttachmentStorageModeCopy));
        Assert.True(editor.SetManagedDropAction(SettingsService.ManagedDropActionFollowWindows));
        Assert.True(editor.SetFileWidgetFolderOpenBehavior(FileWidgetFolderOpenBehaviorNames.Embedded));
        Assert.Equal(3, notified);
        Assert.Equal(SettingsService.AttachmentStorageModeCopy, settings.Settings.QuickCapture.AttachmentStorageMode);
        Assert.Equal(SettingsService.ManagedDropActionFollowWindows, settings.Settings.FileWidget.ManagedDropAction);
        Assert.Equal(FileWidgetFolderOpenBehaviorNames.Embedded, coordinator.ReadFileWidgetFolderOpenBehavior());

        Assert.False(editor.SetAttachmentStorageMode(SettingsService.AttachmentStorageModeCopy));
        Assert.False(editor.SetManagedDropAction(SettingsService.ManagedDropActionFollowWindows));
        Assert.False(editor.SetFileWidgetFolderOpenBehavior(FileWidgetFolderOpenBehaviorNames.Embedded));
        Assert.Equal(3, notified);

        Assert.True(editor.SetAttachmentStorageMode("invalid"));
        Assert.True(editor.SetManagedDropAction("invalid"));
        Assert.True(editor.SetFileWidgetFolderOpenBehavior("invalid"));
        Assert.Equal(6, notified);
        Assert.Equal(SettingsService.AttachmentStorageModeLink, coordinator.ReadAttachmentStorageMode());
        Assert.Equal(SettingsService.ManagedDropActionMove, settings.Settings.FileWidget.ManagedDropAction);
        Assert.Equal(FileWidgetFolderOpenBehaviorNames.Explorer, coordinator.ReadFileWidgetFolderOpenBehavior());
    }

    [Fact]
    public async Task FeatureEnableState_PersistsWhenCallerSaves()
    {
        var settings = new SettingsService(_root);
        await settings.LoadAsync();
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        int notified = 0;
        settings.SettingsChanged += () => notified++;
        coordinator.SetFeatureWidgetEnabled(WidgetKind.Todo, false);
        coordinator.SetFeatureWidgetEnabled(WidgetKind.Search, true);
        Assert.Equal(0, notified);
        await settings.SaveAsync();

        var reloaded = new SettingsService(_root);
        await reloaded.LoadAsync();
        Assert.False(FeatureWidgetSettings.IsEnabled(reloaded.Settings, WidgetKind.Todo));
        Assert.True(FeatureWidgetSettings.IsEnabled(reloaded.Settings, WidgetKind.Search));
    }

    [Fact]
    public async Task StoppedCoordinator_RejectsWritesAndPreservesSavedValues()
    {
        var settings = new SettingsService(_root);
        await settings.LoadAsync();
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        coordinator.SetAttachmentStorageMode(SettingsService.AttachmentStorageModeCopy);
        coordinator.Stop();
        Assert.Throws<ObjectDisposedException>(() => coordinator.SetAttachmentStorageMode(SettingsService.AttachmentStorageModeLink));
        Assert.Throws<ObjectDisposedException>(() => coordinator.SetManagedDropAction(SettingsService.ManagedDropActionCopy));
        Assert.Throws<ObjectDisposedException>(() => coordinator.SetFileWidgetFolderOpenBehavior(FileWidgetFolderOpenBehaviorNames.Embedded));
        Assert.Throws<ObjectDisposedException>(() => coordinator.SetFeatureWidgetEnabled(WidgetKind.Todo, false));
        Assert.True(coordinator.IsStopped);

        await settings.SaveAsync();
        var reloaded = new SettingsService(_root);
        await reloaded.LoadAsync();
        Assert.Equal(SettingsService.AttachmentStorageModeCopy, reloaded.Settings.QuickCapture.AttachmentStorageMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
