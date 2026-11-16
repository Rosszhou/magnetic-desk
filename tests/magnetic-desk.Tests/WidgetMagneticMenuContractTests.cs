namespace MagneticDesk.Tests;

public sealed class WidgetMagneticMenuContractTests
{
    [Fact]
    public void CreationMenu_ExposesNotesAndClipboardInsideTheFeatureSubmenu()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/magnetic-desk/Views/ContentWidgetWindow.Commands.cs"));
        int submenuStart = source.IndexOf("var addFeatureWidgetItem = new MenuFlyoutSubItem", StringComparison.Ordinal);
        Assert.True(submenuStart >= 0);
        int submenuEnd = source.IndexOf("return flyout;", submenuStart, StringComparison.Ordinal);
        string submenu = source[submenuStart..submenuEnd];
        Assert.Contains("Kind: WidgetKind.QuickCapture, Key: \"QuickCapture.Name\"", submenu);
        Assert.Contains("Kind: WidgetKind.Clipboard, Key: \"Clipboard.Title\"", submenu);
        Assert.DoesNotContain("Notes.NewWidget", source);
        Assert.DoesNotContain("Clipboard.NewWidget", source);
        Assert.Contains("SetMagneticGroupCollapseBehavior(_config, behavior)", source);
    }

    [Fact]
    public void TitleAndContentMenus_ExposeMagneticGroups()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/magnetic-desk/Views/ContentWidgetWindow.Commands.cs"));
        Assert.Contains("AppendMagneticGroupMenu(flyout, _config)", source);
        int hostCallback = source.IndexOf("private void ProvideWidgetActionsForContentMenu", StringComparison.Ordinal);
        int callbackEnd = source.IndexOf("private string GetFeatureWidgetCloseMenuText", hostCallback, StringComparison.Ordinal);
        Assert.Contains("AppendMagneticGroupMenu(e.Menu, _config)", source[hostCallback..callbackEnd]);
    }
}
