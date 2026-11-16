using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class WidgetChromeModeResolverTests
{
    [Fact]
    public void Resolve_UsesDisplayGlobalDefaultForSystemMonitor()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.DisplayWidgetChromeMode = SettingsService.WidgetChromeModeHidden;
        var descriptor = TestServices.CreateWidgetContentFactory().GetDescriptor(WidgetKind.SystemMonitor);
        var config = new WidgetConfig { WidgetKind = WidgetKind.SystemMonitor };

        var mode = new WidgetChromeModeResolver(settingsService).Resolve(config, descriptor);

        Assert.Equal(WidgetChromeMode.Hidden, mode);
    }

    [Fact]
    public void Resolve_UsesInteractiveGlobalDefaultForTodo()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.InteractiveWidgetChromeMode = SettingsService.WidgetChromeModeCompact;
        var descriptor = TestServices.CreateWidgetContentFactory().GetDescriptor(WidgetKind.Todo);
        var config = new WidgetConfig { WidgetKind = WidgetKind.Todo };

        var mode = new WidgetChromeModeResolver(settingsService).Resolve(config, descriptor);

        Assert.Equal(WidgetChromeMode.Compact, mode);
    }

    [Fact]
    public void Resolve_InstanceOverrideWinsOverGlobalDefault()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.DisplayWidgetChromeMode = SettingsService.WidgetChromeModeOverlay;
        var descriptor = TestServices.CreateWidgetContentFactory().GetDescriptor(WidgetKind.SystemMonitor);
        var config = new WidgetConfig { WidgetKind = WidgetKind.SystemMonitor };
        WidgetChromeModeNames.SetOverrideMode(config, WidgetChromeMode.Standard);

        var mode = new WidgetChromeModeResolver(settingsService).Resolve(config, descriptor);

        Assert.Equal(WidgetChromeMode.Standard, mode);
    }

    [Fact]
    public void SetOverrideMode_SystemRemovesMetadata()
    {
        var config = new WidgetConfig { WidgetKind = WidgetKind.Todo };
        WidgetChromeModeNames.SetOverrideMode(config, WidgetChromeMode.Hidden);

        WidgetChromeModeNames.SetOverrideMode(config, WidgetChromeMode.System);

        Assert.False(config.Metadata.ContainsKey(WidgetChromeModeNames.MetadataKey));
    }
}
