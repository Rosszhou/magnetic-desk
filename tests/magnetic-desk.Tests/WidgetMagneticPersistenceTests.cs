using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticPersistenceTests
{
    [Fact]
    public void TabGroupClone_PreservesMagneticMembership()
    {
        var original = new WidgetGroupConfig { MagneticGroupId = "magnet" };
        var cloneMethod = typeof(WidgetManager).GetMethod("CloneWidgetGroupConfig",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var copy = (WidgetGroupConfig)cloneMethod.Invoke(null, [original])!;
        Assert.Equal(original.MagneticGroupId, copy.MagneticGroupId);
    }

    [Fact]
    public void SettingsSerializer_PreservesMagneticMembershipAndPositions()
    {
        var settings = new AppSettings();
        settings.Widgets.Add(new WidgetConfig { Id = "first", MagneticGroupId = "magnet", X = 100, Y = 200 });
        settings.Widgets.Add(new WidgetConfig { Id = "second", MagneticGroupId = "magnet", X = 420, Y = 200 });
        settings.WidgetGroups.Add(new WidgetGroupConfig { Id = "tabs", MagneticGroupId = "magnet" });
        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        AppSettings restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        Assert.All(restored.Widgets, widget => Assert.Equal("magnet", widget.MagneticGroupId));
        Assert.Equal(320, restored.Widgets[1].X - restored.Widgets[0].X);
        Assert.Equal("magnet", restored.WidgetGroups[0].MagneticGroupId);
    }

    [Fact]
    public void SettingsSerializer_LoadsOlderWidgetsWithoutMagneticMembership()
    {
        AppSettings restored = JsonSerializer.Deserialize(
            "{\"widgets\":[{\"id\":\"old-widget\"}],\"widgetGroups\":[{\"id\":\"old-group\"}]}",
            SettingsJsonContext.Default.AppSettings)!;
        Assert.Null(restored.Widgets[0].MagneticGroupId);
        Assert.Null(restored.WidgetGroups[0].MagneticGroupId);
    }
}
