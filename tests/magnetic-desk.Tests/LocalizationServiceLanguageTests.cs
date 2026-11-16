using MagneticDesk.Services;
using System.Reflection;

namespace MagneticDesk.Tests;

public sealed class LocalizationServiceLanguageTests
{
    [Theory]
    [InlineData(null, "System")]
    [InlineData("System", "System")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("zh_Hant_HK", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("pt-BR", "en-US")]
    [InlineData("fr-FR", "en-US")]
    [InlineData("ar-SA", "en-US")]
    public void LegacyLanguageSettings_ResolveToSupportedLanguage(string? previous, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguageSetting(previous));
    }

    [Fact]
    public void AvailableLanguages_ContainsSystemChineseAndEnglish()
    {
        string root = Path.Combine(Path.GetTempPath(), "magnetic-desk-language-test-" + Guid.NewGuid());
        try
        {
            var service = new LocalizationService(new SettingsService(root));
            Assert.Equal(new[] { "System", "zh-CN", "en-US" }, service.AvailableLanguageSettings);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SettingsLanguagePicker_UsesSupportedLanguageCatalog()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/magnetic-desk/ViewModels/SettingsViewModel.FeatureOptions.cs"));
        Assert.Contains("AvailableLanguages => _localizationService.AvailableLanguageSettings.ToArray()", source);
    }

    [Fact]
    public void Assembly_ContainsOnlyChineseAndEnglishTranslationTables()
    {
        string[] resources = typeof(LocalizationService).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("MagneticDesk.Strings.", StringComparison.Ordinal))
            .OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "MagneticDesk.Strings.en-US.json", "MagneticDesk.Strings.zh-CN.json" }, resources);
    }

    [Theory]
    [InlineData("ZhCn")]
    [InlineData("EnUs")]
    public void SupportedLanguages_ContainOnboardingAndSettingsCopy(string name)
    {
        var property = typeof(LocalizationService).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Static);
        var table = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(property?.GetValue(null));
        Assert.False(string.IsNullOrWhiteSpace(table["Onboarding.Step1.Title"]));
        Assert.False(string.IsNullOrWhiteSpace(table["Settings.WindowTitle"]));
    }
}
