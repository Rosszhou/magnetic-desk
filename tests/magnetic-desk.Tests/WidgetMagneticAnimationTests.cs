using System.Text.Json;
using System.Xml.Linq;
using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class WidgetMagneticAnimationTests
{
    [Theory]
    [InlineData(null, MagneticAnimationStyle.Classic)]
    [InlineData("", MagneticAnimationStyle.Classic)]
    [InlineData("unknown", MagneticAnimationStyle.Classic)]
    [InlineData("999", MagneticAnimationStyle.Classic)]
    [InlineData("ripple", MagneticAnimationStyle.Ripple)]
    [InlineData("Particles", MagneticAnimationStyle.Particles)]
    [InlineData("Elastic", MagneticAnimationStyle.Elastic)]
    [InlineData("Orbit", MagneticAnimationStyle.Orbit)]
    public void Style_NormalizesLegacyAndUnknownValues(string? value, MagneticAnimationStyle expected)
    {
        Assert.Equal(expected, MagneticDockAnimation.Normalize(value));
    }

    [Theory]
    [InlineData(0, false, "3")]
    [InlineData(.34, false, "2")]
    [InlineData(.67, false, "1")]
    [InlineData(1, false, "1")]
    [InlineData(1, true, "✓")]
    public void Countdown_UsesThreeSecondsForEveryStyle(double progress, bool completed, string expected)
    {
        Assert.Equal(expected, MagneticDockAnimation.CountText(progress, completed));
    }

    [Fact]
    public void AllStyles_ProduceFiniteFramesAndFadeCompletionEffects()
    {
        foreach (var style in Enum.GetValues<MagneticAnimationStyle>())
        foreach (double progress in new[] { 0, .25, .75, 1, double.NaN })
        foreach (double time in new[] { 0, .3, 1, double.NaN, double.MaxValue })
        {
            var frame = MagneticDockAnimation.Calculate(style, progress, true, time);
            Assert.True(double.IsFinite(frame.Attraction));
            Assert.InRange(frame.ScaleX, .5, 1.5);
            Assert.InRange(frame.ScaleY, .5, 1.5);
            Assert.InRange(frame.CompletionOpacity, 0, 1);
            for (int i = 0; i < 6; i++)
            {
                var dot = MagneticDockAnimation.Particle(style, i, progress, true, time);
                Assert.True(double.IsFinite(dot.X) && double.IsFinite(dot.Y));
                Assert.InRange(dot.Opacity, 0, 1);
            }
        }
        Assert.Equal(0, MagneticDockAnimation.Calculate(MagneticAnimationStyle.Ripple, 1, true, .75).CompletionOpacity);
    }

    [Fact]
    public void Styles_HaveDistinctMotionAndVisibleParticles()
    {
        var frames = Enum.GetValues<MagneticAnimationStyle>()
            .Select(s => MagneticDockAnimation.Calculate(s, .5, false, .23)).ToArray();
        Assert.Equal(5, frames.Distinct().Count());
        Assert.False(frames[(int)MagneticAnimationStyle.Orbit].ShowMagnet);
        Assert.Equal(2, Enumerable.Range(0, 6).Count(i =>
            MagneticDockAnimation.Particle(MagneticAnimationStyle.Orbit, i, .5, false, .23).Opacity > 0));
        Assert.All(Enumerable.Range(0, 6), i => Assert.True(
            MagneticDockAnimation.Particle(MagneticAnimationStyle.Particles, i, .5, false, .23).Opacity > 0));
    }

    [Fact]
    public void Badge_HasTransparentBackgroundAndNoInputCapture()
    {
        var document = XDocument.Load(TestPaths.FromRepository("src/magnetic-desk/Controls/WidgetShell.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var badge = document.Descendants().Single(e => (string?)e.Attribute(xaml + "Name") == "MagneticDockBadge");
        Assert.Equal("Transparent", (string?)badge.Attribute("Background"));
        Assert.Equal("0", (string?)badge.Attribute("BorderThickness"));
        Assert.Equal("False", (string?)badge.Attribute("IsHitTestVisible"));
    }

    [Fact]
    public void SettingsSerializer_RestoresTheSelectedEffectAndLegacyDefaults()
    {
        var settings = new AppSettings();
        settings.Widgets.Add(new WidgetConfig { MagneticAnimationStyle = "Orbit" });
        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        Assert.Equal("Orbit", restored.Widgets[0].MagneticAnimationStyle);
        Assert.Null(new WidgetConfig().MagneticAnimationStyle);
    }

    [Fact]
    public void BothEdges_UseTheDraggingWidgetsEffect()
    {
        var source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.MagneticDock.cs"));
        Assert.Contains("ActiveMagneticAnimationStyle ?? Config.MagneticAnimationStyle", source);
        var docking = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Services/WidgetManager.MagneticDock.cs"));
        Assert.Contains("source.Config.MagneticAnimationStyle ?? nameof(MagneticAnimationStyle.Classic)", docking);
        var manager = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Services/WidgetManager.MagneticGroups.cs"));
        Assert.Contains("Enum.GetValues<MagneticAnimationStyle>()", manager);
        Assert.Contains("config.MagneticAnimationStyle = style.ToString()", manager);
    }
}
