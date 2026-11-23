using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed class DesktopOrganizationClassifier
{
    private static readonly char[] ExtensionInputSeparators =
    [
        ' ', '\t', '\r', '\n', ',', ';', '，', '；', '、'
    ];

    private static readonly HashSet<string> ShortcutExtensions =
        CreateSet(".lnk", ".url", ".appref-ms", ".website");

    private static readonly HashSet<string> ImageExtensions =
        CreateSet(
            ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".webp", ".avif", ".jxl",
            ".svg", ".ico", ".heic", ".heif", ".tif", ".tiff",
            ".raw", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf",
            ".psd", ".psb", ".ai", ".eps", ".fig", ".sketch", ".xd", ".indd", ".cdr");

    private static readonly HashSet<string> AudioExtensions =
        CreateSet(
            ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus",
            ".wma", ".amr", ".ape", ".alac", ".aiff", ".mid", ".midi");

    private static readonly HashSet<string> VideoExtensions =
        CreateSet(
            ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".flv",
            ".ts", ".m2ts", ".mpg", ".mpeg", ".3gp", ".rmvb");

    private static readonly HashSet<string> InstallerExtensions =
        CreateSet(
            ".exe", ".msi", ".msix", ".appx", ".appxbundle", ".msixbundle",
            ".apk", ".apks", ".xapk", ".dmg", ".pkg", ".deb", ".rpm", ".cab");

    private static readonly HashSet<string> ArchiveExtensions =
        CreateSet(
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2",
            ".xz", ".txz", ".zst", ".iso", ".img", ".wim", ".vhd", ".vhdx", ".torrent");

    private static readonly HashSet<string> CodeExtensions =
        CreateSet(
            ".c", ".h", ".cpp", ".hpp", ".cc", ".cs", ".csx", ".java", ".kt", ".kts",
            ".go", ".rs", ".py", ".pyw", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
            ".vue", ".svelte", ".html", ".htm", ".css", ".scss", ".less",
            ".json", ".jsonc", ".yaml", ".yml", ".toml", ".xml", ".ini", ".cfg", ".conf", ".env",
            ".sql", ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".bat", ".cmd",
            ".rb", ".php", ".swift", ".dart", ".lua", ".r", ".scala", ".gradle",
            ".sln", ".csproj", ".vcxproj", ".ipynb", ".dockerfile", ".proto");

    private static readonly Dictionary<string, string> DocumentSubtypeByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = DesktopOrganizationSubtypeIds.Pdf,
            [".ofd"] = DesktopOrganizationSubtypeIds.Pdf,
            [".caj"] = DesktopOrganizationSubtypeIds.Pdf,
            [".doc"] = DesktopOrganizationSubtypeIds.Word,
            [".docx"] = DesktopOrganizationSubtypeIds.Word,
            [".docm"] = DesktopOrganizationSubtypeIds.Word,
            [".dotx"] = DesktopOrganizationSubtypeIds.Word,
            [".dotm"] = DesktopOrganizationSubtypeIds.Word,
            [".rtf"] = DesktopOrganizationSubtypeIds.Word,
            [".odt"] = DesktopOrganizationSubtypeIds.Word,
            [".wps"] = DesktopOrganizationSubtypeIds.Word,
            [".wpt"] = DesktopOrganizationSubtypeIds.Word,
            [".pages"] = DesktopOrganizationSubtypeIds.Word,
            [".epub"] = DesktopOrganizationSubtypeIds.Word,
            [".mobi"] = DesktopOrganizationSubtypeIds.Word,
            [".azw3"] = DesktopOrganizationSubtypeIds.Word,
            [".xls"] = DesktopOrganizationSubtypeIds.Excel,
            [".xlsx"] = DesktopOrganizationSubtypeIds.Excel,
            [".xlsm"] = DesktopOrganizationSubtypeIds.Excel,
            [".xlsb"] = DesktopOrganizationSubtypeIds.Excel,
            [".xltx"] = DesktopOrganizationSubtypeIds.Excel,
            [".csv"] = DesktopOrganizationSubtypeIds.Excel,
            [".tsv"] = DesktopOrganizationSubtypeIds.Excel,
            [".ods"] = DesktopOrganizationSubtypeIds.Excel,
            [".et"] = DesktopOrganizationSubtypeIds.Excel,
            [".ett"] = DesktopOrganizationSubtypeIds.Excel,
            [".numbers"] = DesktopOrganizationSubtypeIds.Excel,
            [".ppt"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".pptx"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".pptm"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".ppsx"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".potx"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".odp"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".dps"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".dpt"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".key"] = DesktopOrganizationSubtypeIds.PowerPoint,
            [".txt"] = DesktopOrganizationSubtypeIds.Text,
            [".md"] = DesktopOrganizationSubtypeIds.Text,
            [".markdown"] = DesktopOrganizationSubtypeIds.Text,
            [".log"] = DesktopOrganizationSubtypeIds.Text,
            [".rst"] = DesktopOrganizationSubtypeIds.Text,
            [".org"] = DesktopOrganizationSubtypeIds.Text,
            [".xmind"] = DesktopOrganizationSubtypeIds.Text,
            [".mmap"] = DesktopOrganizationSubtypeIds.Text,
            [".drawio"] = DesktopOrganizationSubtypeIds.Text,
            [".emmx"] = DesktopOrganizationSubtypeIds.Text,
            [".vsdx"] = DesktopOrganizationSubtypeIds.Text,
            [".eml"] = DesktopOrganizationSubtypeIds.Text,
            [".msg"] = DesktopOrganizationSubtypeIds.Text
        };

    public DesktopOrganizationClassification Classify(string path, bool isDirectory = false)
    {
        if (isDirectory)
        {
            return new(DesktopOrganizationCategoryIds.Folders, null, string.Empty);
        }

        string extension = NormalizeExtension(Path.GetExtension(path));

        if (ShortcutExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Shortcuts, null, extension);
        }

        if (DocumentSubtypeByExtension.TryGetValue(extension, out string? documentSubtype))
        {
            return new(DesktopOrganizationCategoryIds.Documents, documentSubtype, extension);
        }

        if (ImageExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Images, null, extension);
        }

        if (AudioExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Media, DesktopOrganizationSubtypeIds.Audio, extension);
        }

        if (VideoExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Media, DesktopOrganizationSubtypeIds.Video, extension);
        }

        if (InstallerExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Packages, DesktopOrganizationSubtypeIds.Installers, extension);
        }

        if (ArchiveExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Packages, DesktopOrganizationSubtypeIds.Archives, extension);
        }

        if (CodeExtensions.Contains(extension))
        {
            return new(DesktopOrganizationCategoryIds.Code, null, extension);
        }

        return new(DesktopOrganizationCategoryIds.Other, null, extension);
    }

    public static IReadOnlyList<string> GetCategoryExtensions(string categoryId) =>
        categoryId switch
        {
            DesktopOrganizationCategoryIds.Shortcuts => ShortcutExtensions.OrderBy(value => value).ToArray(),
            DesktopOrganizationCategoryIds.Documents => DocumentSubtypeByExtension.Keys.OrderBy(value => value).ToArray(),
            DesktopOrganizationCategoryIds.Images => ImageExtensions.OrderBy(value => value).ToArray(),
            DesktopOrganizationCategoryIds.Media => AudioExtensions.Concat(VideoExtensions).OrderBy(value => value).ToArray(),
            DesktopOrganizationCategoryIds.Packages => InstallerExtensions.Concat(ArchiveExtensions).OrderBy(value => value).ToArray(),
            DesktopOrganizationCategoryIds.Code => CodeExtensions.OrderBy(value => value).ToArray(),
            DesktopOrganizationCategoryIds.Folders => [],
            _ => []
        };

    public static IReadOnlyList<string> GetSubtypeExtensions(string subtypeId) =>
        subtypeId switch
        {
            DesktopOrganizationSubtypeIds.Audio => AudioExtensions.OrderBy(value => value).ToArray(),
            DesktopOrganizationSubtypeIds.Video => VideoExtensions.OrderBy(value => value).ToArray(),
            DesktopOrganizationSubtypeIds.Installers => InstallerExtensions.OrderBy(value => value).ToArray(),
            DesktopOrganizationSubtypeIds.Archives => ArchiveExtensions.OrderBy(value => value).ToArray(),
            _ => DocumentSubtypeByExtension
                .Where(pair => string.Equals(pair.Value, subtypeId, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .OrderBy(value => value)
                .ToArray()
        };

    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        string trimmed = extension.Trim();
        while (trimmed.StartsWith('*'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (trimmed.Length == 0 ||
            trimmed.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            return string.Empty;
        }

        string normalized = (trimmed.StartsWith('.') ? trimmed : $".{trimmed}").ToLowerInvariant();
        return normalized.Length > 1 && normalized[1..].Any(char.IsLetterOrDigit)
            ? normalized
            : string.Empty;
    }

    public static IReadOnlyList<string> ParseExtensions(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(ExtensionInputSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeExtension)
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> CreateSet(params string[] extensions) =>
        new(extensions, StringComparer.OrdinalIgnoreCase);
}

public sealed record DesktopOrganizationClassification(
    string CategoryId,
    string? SubtypeId,
    string Extension);
