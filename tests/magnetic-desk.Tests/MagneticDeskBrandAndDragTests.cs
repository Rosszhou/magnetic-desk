using System.Reflection;
using System.Text.Json;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class MagneticDeskBrandAndDragTests
{
    [Theory]
    [InlineData(false, "group", true)]
    [InlineData(true, "group", false)]
    [InlineData(false, null, false)]
    [InlineData(true, null, false)]
    [InlineData(false, "", false)]
    public void DragScope_DefaultMovesGroupAndControlMovesOne(bool control, string? group, bool expected)
    {
        Assert.Equal(expected, WidgetMagneticPlacement.ShouldMoveTogether(control, group));
    }

    [Fact]
    public void DragEntryPoint_CapturesControlAndPassesItToGroupPolicy()
    {
        string source = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/WidgetWindowBase.Interaction.cs"));
        Assert.Contains("bool controlPressed = Win32Helper.IsKeyPressed", source);
        Assert.Contains("TryBeginCoordinatedMove(HWnd, controlPressed)", source);
        Assert.Contains("!controlPressed && !_isCoordinatedMoveDrag", source);
    }

    [Fact]
    public void AssemblyAndConfiguration_UseNewProductIdentity()
    {
        Assembly application = typeof(WidgetManager).Assembly;
        Assert.Equal("magnetic-desk", application.GetName().Name);
        Assert.Equal("magnetic-desk", application.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.EndsWith(Path.DirectorySeparatorChar + "magnetic-desk", new MagneticDeskDataPathService().RootPath);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void VisibleBranding_AndDragHelpUseCurrentNameAndBehavior(string language)
    {
        using JsonDocument resources = JsonDocument.Parse(File.ReadAllText(TestPaths.FromRepository(
            $"src/magnetic-desk/Strings/{language}.json")));
        foreach (string key in new[] { "Settings.WindowTitle", "Tray.Tooltip", "Onboarding.WindowTitle", "Widget.DefaultName" })
        {
            Assert.Contains("magnetic-desk", resources.RootElement.GetProperty(key).GetString()!);
        }
        Assert.DoesNotContain("every widget on the current display", resources.RootElement.GetProperty("Settings.ResizeSnap.InfoTip.Description").GetString()!);
    }
}
