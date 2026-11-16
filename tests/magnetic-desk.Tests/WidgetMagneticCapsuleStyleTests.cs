using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticCapsuleStyleTests
{
    [Fact]
    public void ProductVersion_IsOneZeroZero()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(WidgetConfig).Assembly.GetName().Version);
    }

    [Fact]
    public void RemovedSettings_AreAbsentFromTheSearchCatalog()
    {
        string[] removed = ["Settings.Theme.Title", "Settings.Group.Animation.Title",
            "Settings.Capsule.WidthMode.Title", "Settings.Capsule.Arrangement.Title",
            "Settings.Capsule.Animation.Title", "Settings.About.StoreSupportTitle"];
        Assert.DoesNotContain(SettingsSearchCatalog.Entries, entry => removed.Contains(entry.HeaderKey));
        Assert.Contains(SettingsSearchCatalog.Entries, entry => entry.HeaderKey == "Settings.About.FeedbackTitle");
    }

    [Fact]
    public void FeatureCreationMenu_CreatesKindsWithoutOpeningSettings()
    {
        string source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/ContentWidgetWindow.Commands.cs"));
        int start = source.IndexOf("internal MenuFlyout CreateWidgetCreateFlyout()", StringComparison.Ordinal);
        int end = source.IndexOf("return flyout;", start, StringComparison.Ordinal);
        string menu = source[start..end];
        Assert.Contains("new MenuFlyoutSubItem", menu);
        foreach (string kind in new[] { "QuickCapture", "Clipboard", "Todo", "Search" })
            Assert.Contains("Kind: WidgetKind." + kind, menu);
        Assert.Contains("CreateWidgetOfKindAsync(entry.Kind)", menu);
        Assert.DoesNotContain("ShowSettings", menu);
        Assert.DoesNotContain("Notes.NewWidget", menu);
        Assert.DoesNotContain("Clipboard.NewWidget", menu);
    }

    [Fact]
    public void FrontPin_DefaultsOffAndPersistsThroughTheProductionSerializer()
    {
        Assert.False(new WidgetConfig().IsAlwaysOnTop);
        var settings = new AppSettings();
        settings.Widgets.Add(new WidgetConfig { IsAlwaysOnTop = true });
        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        Assert.True(JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!.Widgets[0].IsAlwaysOnTop);
    }

    [Fact]
    public async Task RemovedUpdater_CannotOfferDownloadOrLaunchAnInstaller()
    {
        var updater = new DisabledAppUpdateService();
        var check = await updater.CheckForUpdatesAsync("1.5.9");
        Assert.False(check.IsUpdateAvailable);
        Assert.Null(check.Manifest);
        Assert.False((await updater.DownloadUpdateAsync(new AppUpdateManifest())).Success);
        Assert.False(updater.StartInstallerHelper("unused.exe").Success);
        Assert.False(updater.IsDownloadInProgress);
        Assert.Empty(updater.ManifestUrl);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("unknown", 0)]
    [InlineData("999", 0)]
    [InlineData("fill", 1)]
    [InlineData("Segments", 2)]
    public void Appearance_NormalizesSavedValues(string? input, int expected) =>
        Assert.Equal((WidgetCapsuleStyle)expected, WidgetCapsuleAppearance.Normalize(input));

    [Theory]
    [InlineData("Ring")]
    [InlineData("Fill")]
    [InlineData("Segments")]
    [InlineData("ClassicBlack")]
    [InlineData("ClassicWhite")]
    public void ProductionSerializer_PreservesAppearance(string style)
    {
        var settings = new AppSettings();
        settings.Widgets.Add(new WidgetConfig { CapsuleStyle = style });
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        Assert.Equal(style, JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!.Widgets[0].CapsuleStyle);
    }

    [Fact]
    public void CompactHomes_RestoreExactlyAfterTemporaryReflow()
    {
        var expanded = new Dictionary<string, RectInt32> { ["a"] = new(100, 100, 500, 300), ["b"] = new(240, 410, 360, 400) };
        var homes = new Dictionary<string, RectInt32> { ["a"] = new(800, 600, 500, 78), ["b"] = new(940, 680, 360, 78) };
        var sizes = homes.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        Assert.Equal(homes, WidgetMagneticCollapseLayout.RestoreCompact(expanded, homes, sizes));
        sizes["a"] = new(600, 78);
        var resized = WidgetMagneticCollapseLayout.RestoreCompact(expanded, homes, sizes);
        Assert.Equal(800, resized["a"].X);
        Assert.Equal(600, resized["a"].Width);
        Assert.Equal(resized["a"].Y + 80, resized["b"].Y);
    }

    [Fact]
    public void CompactHomes_RepairAnAvoidancePositionSavedAsHome()
    {
        var expanded = new Dictionary<string, RectInt32>
        { ["top"] = new(3230, 36, 604, 567), ["bottom"] = new(3364, 899, 420, 600) };
        var homes = new Dictionary<string, RectInt32>
        { ["top"] = new(3230, 36, 604, 78), ["bottom"] = new(3364, 605, 420, 78) };
        var sizes = homes.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        var restored = WidgetMagneticCollapseLayout.RestoreCompact(expanded, homes, sizes);
        Assert.Equal(116, restored["bottom"].Y);
        Assert.Equal(36, restored["top"].Y);
        Assert.Equal(420, restored["bottom"].Width);
        Assert.Equal(restored, WidgetMagneticCollapseLayout.RestoreCompact(expanded, restored, sizes));
    }

    [Fact]
    public void CompactHomes_RoundTripThroughTheProductionSerializer()
    {
        var settings = new AppSettings();
        settings.Widgets.Add(new WidgetConfig { MagneticCompactPlacement = MagneticExpandedPlacement.FromBounds(new(800, 600, 500, 78)) });
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var saved = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!.Widgets[0];
        Assert.Equal(800, saved.MagneticCompactPlacement!.X);
        Assert.Equal(78, saved.MagneticCompactPlacement.Height);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(.6, 3)]
    [InlineData(.01, 1)]
    public void Countdown_UsesRemainingSeconds(double fraction, int seconds) =>
        Assert.Equal(seconds, WidgetCapsuleAppearance.RemainingSeconds(fraction));

    [Fact]
    public void RightAlignedVerticalCapsules_CloseTheirExpandedGap()
    {
        var layout = WidgetMagneticCollapseLayout.Arrange(new[] {
            new MagneticCompactMember("top", new RectInt32(68, 29, 603, 600), new SizeInt32(603, 78)),
            new MagneticCompactMember("bottom", new RectInt32(251, 636, 420, 500), new SizeInt32(420, 78)) });
        Assert.Equal(2, layout["bottom"].Y - layout["top"].Y - layout["top"].Height);
        Assert.Equal(layout["top"].X + layout["top"].Width, layout["bottom"].X + layout["bottom"].Width);
    }
}
