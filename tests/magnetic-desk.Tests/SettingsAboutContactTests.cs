namespace MagneticDesk.Tests;

public sealed class SettingsAboutContactTests
{
    [Fact]
    public void AboutSection_OpensEmailFeedbackAndProvidesUninstall()
    {
        string xaml = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/SettingsWindow.xaml"));
        string feedback = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/SettingsWindow.Feedback.cs"));
        Assert.Contains("ShowFeedbackDialogButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("UninstallButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("cheeseburger2026@qq.com", feedback, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled = true", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitButton_Click", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("BugRadio", feedback, StringComparison.Ordinal);
        Assert.DoesNotContain("SuggestionRadio", feedback, StringComparison.Ordinal);
    }


    private static string[] ProductionSourcesContaining(
        string root,
        string needle,
        params string[] relativeDirectories)
    {
        string projectDirectory = Path.Combine(root, "src", "magnetic-desk");
        IEnumerable<string> searchRoots = relativeDirectories.Length == 0
            ? [projectDirectory]
            : relativeDirectories.Select(directory => Path.Combine(projectDirectory, directory));

        return searchRoots
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path =>
            {
                string relative = Path.GetRelativePath(projectDirectory, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                return !relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith("AppPackages/", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => File.ReadAllText(path).Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .Order()
            .ToArray();
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;

        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "src",
                    "magnetic-desk",
                    "magnetic-desk.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "MagneticDesk repository root was not found.");
    }
}
