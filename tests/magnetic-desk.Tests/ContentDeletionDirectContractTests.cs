namespace MagneticDesk.Tests;

public sealed class ContentDeletionDirectContractTests
{
    [Fact]
    public void TodoDeletionEntryPoints_DoNotRouteThroughConfirmation()
    {
        string source = ReadRepositoryFiles(
            "src/magnetic-desk/Controls/WidgetContents/TodoWidgetContent.xaml.cs",
            "src/magnetic-desk/Controls/WidgetContents/TodoWidgetContent.EditingAndUndo.cs",
            "src/magnetic-desk/Controls/WidgetContents/TodoWidgetContent.ListInteraction.cs",
            "src/magnetic-desk/Controls/WidgetContents/TodoWidgetContent.Menus.cs",
            "src/magnetic-desk/Controls/WidgetContents/TodoWidgetContent.DragDrop.cs");

        Assert.Contains("DeleteItemAsync(", source, StringComparison.Ordinal);
        Assert.Contains("DeleteSelectedItemsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDeleteItemConfirmation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDeleteSelectedConfirmation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowTodoConfirmMenu", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmBeforeDelete", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickCaptureDeletionEntryPoints_DoNotRouteThroughConfirmation()
    {
        string source = ReadRepositoryFiles(
            "src/magnetic-desk/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        Assert.Contains("DeleteQuickCaptureItemAsync(", source, StringComparison.Ordinal);
        Assert.Contains("DeleteSelectedQuickCaptureItemsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmDeleteItemAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowQuickCaptureDeleteConfirmFlyout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowQuickCaptureDeleteSelectedConfirmFlyout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickCapture.DeleteConfirm", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoDeleteConfirmationSetting_IsRemovedFromRuntimeAndSettingsUi()
    {
        string source = ReadRepositoryFiles(
            "src/magnetic-desk/Models/AppSettings.cs",
            "src/magnetic-desk/Services/SettingsService.cs",
            "src/magnetic-desk/ViewModels/SettingsViewModel.cs",
            // Batch 47 moved the Todo switch chain onto the section editor.
            "src/magnetic-desk/Features/Todo/TodoSettingsViewModel.cs",
            "src/magnetic-desk/ViewModels/SettingsViewModel.FeatureOptions.cs",
            "src/magnetic-desk/ViewModels/SettingsViewModel.SettingsSync.cs",
            "src/magnetic-desk/ViewModels/TodoWidgetViewModel.cs",
            "src/magnetic-desk/ViewModels/TodoWidgetViewModel.FilteringAndAppearance.cs",
            "src/magnetic-desk/Views/SettingsWindow.xaml");

        Assert.DoesNotContain("TodoConfirmBeforeDelete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmBeforeDelete", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFiles(params string[] relativePaths)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "src", "magnetic-desk")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName ??
            throw new DirectoryNotFoundException();
        return string.Join(
            Environment.NewLine,
            relativePaths.Select(path => File.ReadAllText(Path.Combine(
                repositoryRoot,
                path.Replace('/', Path.DirectorySeparatorChar)))));
    }
}
