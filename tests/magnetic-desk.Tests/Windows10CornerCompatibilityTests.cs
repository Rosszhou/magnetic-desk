namespace MagneticDesk.Tests;

public sealed class Windows10CornerCompatibilityTests
{
    [Fact]
    public void EffectiveCornerPolicy_IsUsedByWindowCapsuleAndPopupSurfaces()
    {
        string widgetBounds = Read("src/magnetic-desk/Views/WidgetWindowBase.Bounds.cs");
        string widgetCollapse = Read("src/magnetic-desk/Views/WidgetWindowBase.Collapse.cs");
        string searchPopup = Read("src/magnetic-desk/Views/SearchPopupWindow.xaml.cs");
        string stackPopover = Read(
            "src/magnetic-desk/Controls/WidgetContents/FileSurfaceContent.StackPopover.cs");

        Assert.Contains(
            "ResolveEffectiveWidgetCornerPreference",
            widgetBounds,
            StringComparison.Ordinal);
        Assert.Contains(
            "ResolveEffectiveWidgetCornerPreference",
            widgetCollapse,
            StringComparison.Ordinal);
        Assert.Contains(
            "ResolveEffectiveWidgetCompactMediaCornerMode",
            widgetCollapse,
            StringComparison.Ordinal);
        Assert.Contains(
            "ResolveEffectiveWidgetCornerPreference",
            searchPopup,
            StringComparison.Ordinal);
        Assert.Contains(
            "ResolveEffectiveWidgetCornerPreference",
            stackPopover,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Win10Compatibility_PreservesPersistedRequestedCornerPreference()
    {
        string compatibility = Read(
            "src/magnetic-desk/Services/WindowsCompatibilityService.cs");
        string settings = Read("src/magnetic-desk/Services/SettingsService.cs");

        Assert.Contains(
            "ResolveEffectiveWidgetCornerPreferenceForBuild",
            compatibility,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveEffectiveWidgetCornerPreference",
            settings,
            StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(TestPaths.FromRepository(relativePath));
}
