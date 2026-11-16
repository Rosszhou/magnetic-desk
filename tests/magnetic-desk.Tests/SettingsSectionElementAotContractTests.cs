using System.Text.RegularExpressions;

namespace MagneticDesk.Tests;

/// <summary>
/// Under Native AOT, CsWinRT resolves the RCW class for a FindName result
/// by reflecting on the runtime class name; a WinUI control the app never
/// constructs in C# (PasswordBox) has no reflection metadata there, so the
/// RCW comes back as a base class and a plain cast throws
/// InvalidCastException (feedback: WebDAV "Specified cast is not valid").
/// Every deferred-section accessor must go through the typed helper, which
/// re-wraps the native object with the statically known type instead.
/// </summary>
public sealed class SettingsSectionElementAotContractTests
{
    private static readonly Regex RawCastAccessor = new(
        @"\(\s*global::[A-Za-z0-9_.]+\s*\)\s*FindCreatedSectionElement\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex SectionElementCallLiteral = new(
        @"FindCreatedSectionElement(?:<[^>]+?>)?\s*\(\s*""(?<tag>[^""]+)""\s*,\s*""(?<name>[^""]+)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex XamlNameAttribute = new(
        @"x:Name=""(?<name>[^""]+)""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void SectionElementAccessors_NeverCastFindNameResultsDirectly()
    {
        string source = ReadRepositoryFile("src/magnetic-desk/Views/SettingsWindow.SectionElements.cs");

        Assert.DoesNotMatch(RawCastAccessor, source);
        Assert.Contains("MarshalInspectable<T>.FromAbi", source, StringComparison.Ordinal);
        Assert.Contains(
            "FindCreatedSectionElement<global::MagneticDesk.Views.SettingsSections.AppearanceSettingsSection>(\"Appearance\", \"AppearanceSection\")",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SectionElementLookupLiterals_ResolveToNamedElementsInSettingsWindowXaml()
    {
        // Lookups pass the element name as a string literal, including inline
        // calls that bypass the SectionElements property table (e.g.
        // CloudBackupSnapshotsEmptyHint). A mistyped literal resolves to a
        // silent null under AOT — FindCreatedSectionElement just reports the
        // element as never created — so every literal must match a real
        // x:Name in the window XAML. The section tag is a runtime dictionary
        // key (see DeferredSections), not a XAML attribute, so only the name
        // side can be cross-checked here.
        string xaml = ReadRepositoryFile("src/magnetic-desk/Views/SettingsWindow.xaml");
        var xamlNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in XamlNameAttribute.Matches(xaml))
        {
            xamlNames.Add(match.Groups["name"].Value);
        }

        string[] lookupSources =
        [
            "src/magnetic-desk/Views/SettingsWindow.SectionElements.cs",
            "src/magnetic-desk/Views/SettingsWindow.Maintenance.cs",
        ];

        int checkedLiterals = 0;
        foreach (string relativePath in lookupSources)
        {
            string source = ReadRepositoryFile(relativePath);
            foreach (Match match in SectionElementCallLiteral.Matches(source))
            {
                checkedLiterals++;
                string name = match.Groups["name"].Value;
                Assert.True(
                    xamlNames.Contains(name),
                    $"{relativePath} looks up \"{name}\" but SettingsWindow.xaml has no x:Name=\"{name}\".");
            }
        }

        // The scan must stay wired to real call sites; an empty or broken
        // regex would otherwise pass vacuously.
        Assert.True(checkedLiterals > 0);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }
}
