using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Services;
using Windows.Graphics;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticCollapseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SequentialExpansion_UsesTheCompactGroupOriginAndSnapSpacing(bool topFirst)
    {
        var expanded = new Dictionary<string, RectInt32>
        {
            ["top"] = new(3230, 36, 604, 567),
            ["bottom"] = new(3364, 899, 420, 600)
        };
        var origin = new PointInt32(3230, 36);
        var sizes = expanded.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, 78));
        string first = topFirst ? "top" : "bottom";
        sizes[first] = new(expanded[first].Width, expanded[first].Height);
        var mixed = WidgetMagneticCollapseLayout.ArrangeVisible(expanded, sizes, true, origin: origin, expandedGap: 8);
        Assert.Equal(36, mixed["top"].Y);
        Assert.Equal(mixed["top"].Y + mixed["top"].Height + 2, mixed["bottom"].Y);
        sizes = expanded.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        var opened = WidgetMagneticCollapseLayout.ArrangeVisible(expanded, sizes, false, origin: origin, expandedGap: 8);
        Assert.Equal(new RectInt32(3230, 36, 604, 567), opened["top"]);
        Assert.Equal(new RectInt32(3364, 611, 420, 600), opened["bottom"]);
        Assert.Equal(899, expanded["bottom"].Y);
    }

    [Fact]
    public void PublishedResources_ContainTheNewNamesAndScopeLabels()
    {
        using var stream = typeof(WidgetConfig).Assembly.GetManifestResourceStream("MagneticDesk.Strings.zh-CN.json");
        Assert.NotNull(stream);
        using var strings = JsonDocument.Parse(stream!);
        Assert.Equal("便签", strings.RootElement.GetProperty("QuickCapture.Name").GetString());
        Assert.Equal("编写、编辑和固定便签", strings.RootElement.GetProperty("WidgetContent.QuickCapture.StatusDescription").GetString());
        Assert.Equal("整个磁吸组合", strings.RootElement.GetProperty("Magnetic.Collapse.Group").GetString());
        Assert.Equal("新建便签", strings.RootElement.GetProperty("Notes.NewWidget").GetString());
        Assert.Equal("剪贴板", strings.RootElement.GetProperty("Clipboard.Title").GetString());
    }

    private static MagneticCompactMember Member(string id, int x, int y, int width = 180) =>
        new(id, new RectInt32(x, y, 400, 500), new SizeInt32(width, 48));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MixedPair_ReflowsTheCompactNeighborWithoutOverlap(bool vertical, bool openFirst)
    {
        var expanded = new Dictionary<string, RectInt32>
        {
            ["a"] = new(100, 100, 500, 400),
            ["b"] = vertical ? new(240, 510, 360, 400) : new(610, 100, 360, 400)
        };
        string open = openFirst ? "a" : "b";
        string compact = openFirst ? "b" : "a";
        var sizes = expanded.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        sizes[compact] = new(sizes[compact].Width, 78);
        var result = WidgetMagneticCollapseLayout.ArrangeVisible(expanded, sizes, true, open, expanded[open]);
        Assert.Equal(expanded[open], result[open]);
        Assert.Equal(78, result[compact].Height);
        if (vertical) Assert.Equal(result["a"].Y + result["a"].Height + 2, result["b"].Y);
        else Assert.Equal(result["a"].X + result["a"].Width + 2, result["b"].X);
    }

    [Fact]
    public void MixedLShape_KeepsTheCapsulesBesideAndBelowTheOpenPanel()
    {
        var expanded = new Dictionary<string, RectInt32>
        { ["a"] = new(100, 100, 500, 600), ["b"] = new(610, 100, 400, 300), ["c"] = new(610, 410, 400, 290) };
        var sizes = new Dictionary<string, SizeInt32>
        { ["a"] = new(500, 78), ["b"] = new(400, 300), ["c"] = new(400, 78) };
        var result = WidgetMagneticCollapseLayout.ArrangeVisible(expanded, sizes, true, "b", expanded["b"]);
        Assert.Equal(result["a"].X + result["a"].Width + 2, result["b"].X);
        Assert.Equal(result["b"].Y + result["b"].Height + 2, result["c"].Y);
        Assert.Equal(result["b"].X, result["c"].X);
    }

    [Fact]
    public void MixedReflow_DoesNotMutateTheSavedExpandedTopology()
    {
        var expanded = new Dictionary<string, RectInt32>
        { ["a"] = new(100, 100, 500, 300), ["b"] = new(240, 410, 360, 400) };
        var original = expanded.ToDictionary();
        var compact = expanded.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, 78));
        var first = WidgetMagneticCollapseLayout.ArrangeVisible(expanded, compact, true);
        for (int i = 0; i < 4; i++)
        {
            var mixed = compact.ToDictionary();
            mixed["a"] = new(500, 300);
            WidgetMagneticCollapseLayout.ArrangeVisible(expanded, mixed, true, "a", expanded["a"]);
            Assert.Equal(first, WidgetMagneticCollapseLayout.ArrangeVisible(expanded, compact, true));
        }
        Assert.Equal(original, expanded);
    }

    [Fact]
    public void LShape_CompressesTheEmptyExpandedSpace()
    {
        var result = WidgetMagneticCollapseLayout.Arrange(new[]
        {
            Member("notes", 100, 100), Member("files", 510, 100), Member("todo", 510, 610)
        });
        Assert.Equal(100, result["notes"].X);
        Assert.Equal(100, result["files"].Y);
        Assert.Equal(result["notes"].X + result["notes"].Width + 2, result["files"].X);
        Assert.Equal(result["files"].X, result["todo"].X);
        Assert.Equal(result["files"].Y + result["files"].Height + 2, result["todo"].Y);
    }

    [Theory]
    [InlineData(100, 700, true)]
    [InlineData(700, 100, false)]
    public void Pair_PreservesTheAttachedAxis(int x, int y, bool vertical)
    {
        var result = WidgetMagneticCollapseLayout.Arrange(new[] { Member("a", 100, 100), Member("b", x, y) });
        Assert.Equal(vertical, result["a"].X == result["b"].X);
        Assert.Equal(!vertical, result["a"].Y == result["b"].Y);
    }

    [Fact]
    public void Column_PreservesEachConfiguredWidth()
    {
        var result = WidgetMagneticCollapseLayout.Arrange(new[] { Member("a", -600, 100, 140), Member("b", -597, 700, 230) });
        Assert.Equal(140, result["a"].Width);
        Assert.Equal(230, result["b"].Width);
        Assert.Equal(-600, result["b"].X);
    }

    [Fact]
    public void TShape_KeepsTheLowerMemberInTheMiddle()
    {
        var result = WidgetMagneticCollapseLayout.Arrange(new[]
        { Member("a", 10, 10), Member("b", 420, 10), Member("c", 830, 10), Member("d", 420, 520) });
        Assert.True(result["a"].X < result["b"].X && result["b"].X < result["c"].X);
        Assert.Equal(result["b"].X, result["d"].X);
    }

    [Fact]
    public void LegacyLayout_DefaultsToIndependentCollapse()
    {
        var config = JsonSerializer.Deserialize<WidgetConfig>("{\"MagneticGroupId\":\"a\",\"WidgetKind\":\"QuickCapture\"}")!;
        Assert.False(config.MagneticCollapseLinked);
        Assert.Null(config.MagneticExpandedPlacement);
        Assert.Equal(WidgetKind.QuickCapture, config.WidgetKind);
    }

    [Fact]
    public void GroupLayout_RoundTripsExpandedPlacement()
    {
        var config = new WidgetConfig { MagneticGroupId = "g", MagneticCollapseLinked = true,
            MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(new RectInt32(-500, 100, 400, 600)) };
        var restored = JsonSerializer.Deserialize<WidgetConfig>(JsonSerializer.Serialize(config))!;
        Assert.True(restored.MagneticCollapseLinked);
        Assert.Equal(-500, restored.MagneticExpandedPlacement!.X);
        Assert.Equal(600, restored.MagneticExpandedPlacement.Height);
    }

    [Fact]
    public void ProductionSerializer_PreservesGroupScopeAndClipboard()
    {
        var settings = new AppSettings();
        settings.Widgets.Add(new WidgetConfig { WidgetKind = WidgetKind.Clipboard,
            MagneticCollapseLinked = true,
            MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(new RectInt32(80, 60, 400, 500)) });
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        Assert.Equal(WidgetKind.Clipboard, restored.Widgets[0].WidgetKind);
        Assert.True(restored.Widgets[0].MagneticCollapseLinked);
        Assert.Equal(500, restored.Widgets[0].MagneticExpandedPlacement!.Height);
    }

    [Theory]
    [InlineData(WidgetKind.QuickCapture, QuickCaptureViewMode.Recent, QuickCaptureViewMode.Records)]
    [InlineData(WidgetKind.QuickCapture, QuickCaptureViewMode.Pinned, QuickCaptureViewMode.Pinned)]
    [InlineData(WidgetKind.Clipboard, QuickCaptureViewMode.Records, QuickCaptureViewMode.Recent)]
    [InlineData(WidgetKind.Clipboard, QuickCaptureViewMode.Pinned, QuickCaptureViewMode.Recent)]
    public void NotesAndClipboard_HaveIndependentViews(WidgetKind kind, QuickCaptureViewMode input, QuickCaptureViewMode expected)
    {
        Assert.Equal(expected, QuickCaptureChannelPolicy.NormalizeView(kind, input));
    }

    [Fact]
    public void Search_OnlyReturnsTheSelectedChannel()
    {
        var note = new QuickCaptureItem { Id = "note" };
        var copied = new QuickCaptureItem { Id = "clipboard" };
        Assert.Same(note, Assert.Single(QuickCaptureChannelPolicy.SearchItems(WidgetKind.QuickCapture, new[] { note }, new[] { copied })));
        Assert.Same(copied, Assert.Single(QuickCaptureChannelPolicy.SearchItems(WidgetKind.Clipboard, new[] { note }, new[] { copied })));
    }

    [Fact]
    public void ClipboardKind_PreservesLegacyNumericValues()
    {
        Assert.Equal(1, (int)WidgetKind.QuickCapture);
        Assert.Equal(9, (int)WidgetKind.Glance);
        Assert.Equal(10, (int)WidgetKind.Clipboard);
        Assert.True(WidgetRegistry.Default.CanCreateWindow(WidgetKind.Clipboard));
        Assert.Equal(WidgetKind.Clipboard, JsonSerializer.Deserialize<WidgetKind>("\"Clipboard\""));
    }

    [Fact]
    public void OffscreenGroup_ReturnsToTheWorkAreaTogether()
    {
        var targets = new Dictionary<string, RectInt32>
        { ["a"] = new RectInt32(-800, 100, 200, 50), ["b"] = new RectInt32(-598, 100, 200, 50) };
        var visible = WidgetMagneticCollapseLayout.KeepVisible(targets, new RectInt32(0, 0, 1920, 1080));
        Assert.Equal(0, visible["a"].X);
        Assert.Equal(202, visible["b"].X);
    }

    [Fact]
    public async Task ClipboardDisableAndRecording_PersistIndependentlyOfNotes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "magnetic-desk.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(directory);
            FeatureWidgetSettings.SetEnabled(settings.Settings, WidgetKind.QuickCapture, false);
            settings.Settings.QuickCaptureClipboardEnabled = true;
            settings.Settings.Widgets.Add(new WidgetConfig { WidgetKind = WidgetKind.Clipboard, IsDisabled = true, IsVisible = false });
            await settings.SaveAsync();
            var restored = new SettingsService(directory);
            await restored.LoadAsync();
            Assert.False(FeatureWidgetSettings.IsEnabled(restored.Settings, WidgetKind.QuickCapture));
            Assert.True(restored.Settings.QuickCaptureClipboardEnabled);
            Assert.True(Assert.Single(restored.Settings.Widgets.Where(w => w.WidgetKind == WidgetKind.Clipboard)).IsDisabled);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
