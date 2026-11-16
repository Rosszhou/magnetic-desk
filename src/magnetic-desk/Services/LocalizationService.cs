using System.Globalization;
using System.Reflection;
using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed class LocalizationService
{
    public const string LanguageSystem = SettingsService.LanguageSystem;
    public const string LanguageChinese = SettingsService.LanguageChinese;
    public const string LanguageChineseTraditional = SettingsService.LanguageChineseTraditional;
    public const string LanguageEnglish = SettingsService.LanguageEnglish;
    public const string LanguageJapanese = "ja-JP";
    public const string LanguageGerman = "de-DE";
    public const string LanguagePortuguese = "pt-BR";
    public const string LanguageHindi = SettingsService.LanguageHindi;
    public const string LanguageSpanish = SettingsService.LanguageSpanish;
    public const string LanguageFrench = SettingsService.LanguageFrench;
    public const string LanguageArabic = SettingsService.LanguageArabic;
    public const string LanguageBengali = SettingsService.LanguageBengali;
    public const string LanguageRussian = SettingsService.LanguageRussian;
    public const string LanguageKorean = SettingsService.LanguageKorean;

    private readonly SettingsService _settingsService;

    public event Action? LanguageChanged;

    public LocalizationService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string LanguageSetting => NormalizeLanguageSetting(_settingsService.Settings.Language);

    public string CurrentCultureName
    {
        get
        {
            string language = LanguageSetting;
            if (language == LanguageSystem)
            {
                language = ResolveDefaultLanguage();
            }

            return language;
        }
    }

    public bool IsEnglish => string.Equals(CurrentCultureName, LanguageEnglish, StringComparison.OrdinalIgnoreCase);
    public bool IsChinese => CurrentCultureName is LanguageChinese or LanguageChineseTraditional;
    public bool IsTraditionalChinese => CurrentCultureName == LanguageChineseTraditional;

    /// <summary>
    /// Returns the 2-letter language code "en" or "zh"
    /// suitable for passing to weather and location APIs.
    /// </summary>
    public string ApiLanguageCode => IsChinese ? "zh" : "en";

    public IReadOnlyList<string> AvailableLanguageSettings { get; } =
    [
        LanguageSystem,
        LanguageChinese,
        LanguageEnglish
    ];

    public string GetLanguageDisplayName(string language)
    {
        return NormalizeLanguageSetting(language) switch
        {
            LanguageChinese => "简体中文",
            LanguageEnglish => T("Language.English"),
            _ => T("Language.System")
        };
    }

    public void SetLanguage(string language)
    {
        string normalizedLanguage = NormalizeLanguageSetting(language);
        if (string.Equals(_settingsService.Settings.Language, normalizedLanguage, StringComparison.Ordinal))
        {
            return;
        }

        _settingsService.Settings.Language = normalizedLanguage;
        try
        {
            _settingsService.SaveDebounced();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] SaveDebounced failed during language switch: {ex.Message}");
        }
        // RaiseLanguageChanged must always be called, even if SaveDebounced
        // failed, so that the UI updates to the new language immediately.
        RaiseLanguageChanged();
    }

    /// <summary>
    /// Invokes all LanguageChanged handlers, catching exceptions per-handler
    /// so that one failing subscriber does not prevent subsequent subscribers
    /// from receiving the notification.
    /// </summary>
    private void RaiseLanguageChanged()
    {
        if (LanguageChanged is null)
        {
            return;
        }

        foreach (var handler in LanguageChanged.GetInvocationList())
        {
            try
            {
                ((Action)handler).Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LocalizationService] LanguageChanged handler '{handler.Method.DeclaringType?.Name}.{handler.Method.Name}' threw: {ex.Message}");
            }
        }
    }

    public string T(string key)
    {
        var table = IsEnglish ? EnUs : ZhCn;

        if (table.TryGetValue(key, out string? value))
        {
            return value;
        }

        // All shipped locale files are expected to be complete. Keep English
        // as a defensive fallback for unknown keys in future app versions.
        if (EnUs.TryGetValue(key, out value))
        {
            return value;
        }

        return ZhCn.TryGetValue(key, out value) ? value : key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key), args);
    }

    public static string DefaultText(string key)
    {
        return ZhCn.TryGetValue(key, out string? value) ? value : key;
    }

    public static string DefaultFormat(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, DefaultText(key), args);
    }

    public static string NormalizeLanguageSetting(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            string.Equals(language, LanguageSystem, StringComparison.OrdinalIgnoreCase))
        {
            return LanguageSystem;
        }
        return language.Replace('_', '-').StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? LanguageChinese
            : LanguageEnglish;
    }

    private static string ResolveSystemLanguage() =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? LanguageChinese
            : LanguageEnglish;

    internal static bool IsTraditionalChineseCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return false;
        }

        string normalized = cultureName.Replace('_', '-');
        return normalized.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("zh-MO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the default language used when the user has not explicitly
    /// picked one in the app. Honors the language chosen at install time
    /// (the Inno Setup installer writes it to
    /// HKCU\Software\magnetic-desk\InstallLanguage), falling back to the OS UI
    /// culture when that value is absent or unrecognized.
    /// </summary>
    private static string ResolveDefaultLanguage()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\magnetic-desk",
                "InstallLanguage",
                null) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return NormalizeLanguageSetting(value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocalizationService] Failed reading InstallLanguage registry key: {ex.Message}");
        }

        return ResolveSystemLanguage();
    }

    private static Dictionary<string, string>? _zhCn;
    private static Dictionary<string, string>? _enUs;
    private static readonly object s_loadLock = new();

    private static Dictionary<string, string> ZhCn
    {
        get
        {
            if (_zhCn is not null) return _zhCn;
            lock (s_loadLock)
            {
                _zhCn ??= LoadStringResource("MagneticDesk.Strings.zh-CN.json");
            }
            return _zhCn;
        }
    }

    private static Dictionary<string, string> EnUs
    {
        get
        {
            if (_enUs is not null) return _enUs;
            lock (s_loadLock)
            {
                _enUs ??= LoadStringResource("MagneticDesk.Strings.en-US.json");
            }
            return _enUs;
        }
    }

    private static Dictionary<string, string> LoadStringResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Resource not found: {resourceName}");
            return [];
        }
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize(
            json,
            LocalizationJsonContext.Default.LocalizedStrings) ?? [];
    }

#if MAGNETIC_DESK_NATIVE_AOT && MAGNETIC_DESK_AOT_SMOKE_HARNESS
    internal static IReadOnlyList<AotLocaleResourceDiagnostic> CaptureAotSmokeResourceDiagnostics()
    {
        return
        [
            CaptureAotSmokeResource("zh-CN", ZhCn),
            CaptureAotSmokeResource("en-US", EnUs),
        ];
    }

    private static AotLocaleResourceDiagnostic CaptureAotSmokeResource(
        string locale,
        IReadOnlyDictionary<string, string> resources)
    {
        return new AotLocaleResourceDiagnostic(
            locale,
            resources.Count,
            // The settings window title key was renamed by the settings-entry
            // consolidation (bc8fd2eb retired "Window.Settings.Title"); the
            // smoke keeps requiring the live key, "Settings.WindowTitle".
            resources.TryGetValue("Settings.WindowTitle", out string? settingsTitle) &&
                !string.IsNullOrWhiteSpace(settingsTitle),
            resources.TryGetValue("Search.Action.OpenSettings", out string? openSettings) &&
                !string.IsNullOrWhiteSpace(openSettings));
    }
#endif
}

#if MAGNETIC_DESK_NATIVE_AOT && MAGNETIC_DESK_AOT_SMOKE_HARNESS
internal sealed record AotLocaleResourceDiagnostic(
    string Locale,
    int ResourceCount,
    bool HasSettingsTitle,
    bool HasOpenSettingsAction);
#endif

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(
    typeof(Dictionary<string, string>),
    TypeInfoPropertyName = "LocalizedStrings")]
internal sealed partial class LocalizationJsonContext : JsonSerializerContext
{
}
