using System.Runtime.InteropServices;
using MagneticDesk.Helpers;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed class DesktopOrganizationScanner
{
    // Files up to and including 100 MiB are eligible for quick organization.
    // Keep the comparison in CreateSnapshot strictly greater-than so the
    // advertised limit is inclusive for the boundary file.
    public const long SlowItemThresholdBytes = 100L * 1024 * 1024;
    public const long QuickBatchSizeLimitBytes = 100L * 1024 * 1024;
    public const int QuickBatchItemLimit = 200;

    private const uint FileAttributeRecallOnOpen = 0x00040000;
    private const uint FileAttributeRecallOnDataAccess = 0x00400000;
    private const uint IoReparseTagCloudMask = 0xFFFF0FFF;
    private const uint IoReparseTagCloudBase = 0x9000001A;

    private static readonly string[] TemporarySuffixes =
    [
        ".tmp",
        ".temp",
        ".part",
        ".partial",
        ".crdownload",
        ".download",
        ".opdownload",
        ".aria2",
        ".!ut",
        ".bc!",
        ".!qb",
        ".td",
        ".td.cfg",
        ".xltd",
        ".xltd.cfg",
        ".fdmdownload",
        ".downloading",
        ".crswap",
        ".filepart",
        ".unconfirmed"
    ];

    private static readonly string[] ActiveDownloadSidecarSuffixes =
    [
        ".aria2",
        ".td.cfg",
        ".xltd.cfg",
        ".fdmdownload"
    ];

    private static readonly string[] IgnoredSystemFileNames =
    [
        "desktop.ini",
        "thumbs.db",
        ".ds_store"
    ];

    private readonly DesktopOrganizationClassifier _classifier;
    private readonly Func<string> _desktopPathProvider;
    private readonly Func<string> _publicDesktopPathProvider;

    public DesktopOrganizationScanner(
        DesktopOrganizationClassifier classifier,
        Func<string>? desktopPathProvider = null,
        Func<string>? publicDesktopPathProvider = null)
    {
        _classifier = classifier;
        _desktopPathProvider = desktopPathProvider ??
            (() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        _publicDesktopPathProvider = publicDesktopPathProvider ??
            (() => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    }

    public Task<DesktopOrganizationScanResult> ScanAsync(
        bool includeSlowItems = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(includeSlowItems, cancellationToken), cancellationToken);
    }

    internal DesktopOrganizationFileSnapshot CreateAutoOrganizationSnapshot(string path) =>
        CreateSnapshot(
            path,
            NormalizeOptionalPath(_publicDesktopPathProvider()),
            includeSlowItems: false,
            excludeDirectoriesByDefault: true);

    private DesktopOrganizationScanResult Scan(bool includeSlowItems, CancellationToken cancellationToken)
    {
        string desktopPath = Path.GetFullPath(_desktopPathProvider());
        string publicDesktopPath = NormalizeOptionalPath(_publicDesktopPathProvider());
        var items = new List<DesktopOrganizationFileSnapshot>();

        ScanSource(desktopPath, DesktopOrganizationSourceScope.Personal);
        bool publicUnavailable = string.IsNullOrWhiteSpace(publicDesktopPath);
        if (!publicUnavailable && !string.Equals(
                Path.TrimEndingDirectorySeparator(desktopPath),
                Path.TrimEndingDirectorySeparator(publicDesktopPath),
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                publicUnavailable = !Directory.Exists(publicDesktopPath);
                ScanSource(publicDesktopPath, DesktopOrganizationSourceScope.Public);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                publicUnavailable = true;
                App.Log($"[DesktopOrganization] Public desktop scan unavailable: {ex.Message}");
            }
        }

        return new DesktopOrganizationScanResult
        {
            DesktopPath = desktopPath,
            PublicDesktopPath = publicDesktopPath,
            PublicDesktopUnavailable = publicUnavailable,
            Items = items
        };

        void ScanSource(string root, DesktopOrganizationSourceScope scope)
        {
            if (!Directory.Exists(root)) return;
            var sourceItems = new List<DesktopOrganizationFileSnapshot>();
            foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Manual scanning surfaces regular folders directly under the
                // Folders category (users can uncheck the Folders card in one
                // click). Auto organization still passes excludeDirectoriesByDefault: true
                // so folders are only auto-moved when an enabled rule explicitly claims Folders.
                sourceItems.Add(CreateSnapshot(
                    path,
                    string.Empty,
                    includeSlowItems,
                    excludeDirectoriesByDefault: false) with { SourceScope = scope });
            }
            if (!includeSlowItems) ApplyQuickBatchLimit(sourceItems);
            items.AddRange(sourceItems);
        }
    }

    private static void ApplyQuickBatchLimit(
        IList<DesktopOrganizationFileSnapshot> items)
    {
        long acceptedBytes = 0;
        int acceptedCount = 0;
        foreach (int index in items
                     .Select((item, index) => new { item, index })
                     .Where(entry => entry.item.IsEligible)
                     .OrderBy(entry => entry.item.Size)
                     .ThenBy(entry => entry.item.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(entry => entry.index))
        {
            DesktopOrganizationFileSnapshot item = items[index];
            if (acceptedCount >= QuickBatchItemLimit ||
                acceptedBytes + item.Size > QuickBatchSizeLimitBytes)
            {
                items[index] = item with
                {
                    ExclusionReason = DesktopOrganizationExclusionReason.BatchLimit
                };
                continue;
            }

            acceptedCount++;
            acceptedBytes += item.Size;
        }
    }

    private DesktopOrganizationFileSnapshot CreateSnapshot(
        string path,
        string publicDesktopPath,
        bool includeSlowItems,
        bool excludeDirectoriesByDefault = false)
    {
        string fullPath = Path.GetFullPath(path);
        string name = Path.GetFileName(fullPath);
        DesktopOrganizationClassification classification = _classifier.Classify(fullPath, isDirectory: false);
        long size = 0;
        DateTime lastWriteTimeUtc = DateTime.MinValue;
        DesktopOrganizationExclusionReason reason;
        bool isDirectory = false;

        try
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            isDirectory = (attributes & FileAttributes.Directory) != 0;
            classification = _classifier.Classify(fullPath, isDirectory);

            bool isHiddenOrSystem = IsHiddenOrSystemEntry(fullPath, name, attributes, isDirectory);
            bool isOfflineOrCloudPlaceholder = IsOfflineOrCloudPlaceholder(attributes);
            bool isUnsafeReparsePoint = !isOfflineOrCloudPlaceholder &&
                                        IsUnsafeReparsePoint(fullPath, attributes);
            bool isTemporary = (attributes & FileAttributes.Temporary) != 0 ||
                               HasTemporaryOrLockName(name) ||
                               (!isDirectory && HasActiveDownloadSidecar(fullPath));

            // Safety classifications take precedence over the optional folder
            // classification. A junction, hidden system folder, cloud-only
            // placeholder, or temporary directory must never become selectable
            // merely because it also carries the Directory attribute.
            // Offline/recall attributes are checked before reparse points so
            // OneDrive Files On-Demand placeholders report OfflinePlaceholder,
            // while locally hydrated OneDrive files remain eligible.
            reason = isHiddenOrSystem
                ? DesktopOrganizationExclusionReason.HiddenOrSystem
                : isOfflineOrCloudPlaceholder
                    ? DesktopOrganizationExclusionReason.OfflinePlaceholder
                    : isUnsafeReparsePoint
                        ? DesktopOrganizationExclusionReason.ReparsePoint
                        : isTemporary
                            ? DesktopOrganizationExclusionReason.TemporaryOrDownloading
                            : IsUnderPath(fullPath, publicDesktopPath)
                                ? DesktopOrganizationExclusionReason.PublicDesktopItem
                                : isDirectory && excludeDirectoriesByDefault
                                    ? DesktopOrganizationExclusionReason.Folder
                                    : DesktopOrganizationExclusionReason.None;

            if (!isDirectory)
            {
                var file = new FileInfo(fullPath);
                size = file.Length;
                lastWriteTimeUtc = file.LastWriteTimeUtc;
                if (reason == DesktopOrganizationExclusionReason.None &&
                    !includeSlowItems &&
                    size > SlowItemThresholdBytes)
                {
                    reason = DesktopOrganizationExclusionReason.SlowItem;
                }
            }
            else
            {
                lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(fullPath);
            }
        }
        catch
        {
            reason = DesktopOrganizationExclusionReason.Unavailable;
        }

        return new DesktopOrganizationFileSnapshot(
            fullPath,
            name,
            classification.Extension,
            size,
            lastWriteTimeUtc,
            classification.CategoryId,
            classification.SubtypeId,
            reason,
            isDirectory);
    }

    internal static bool IsOfflineOrCloudPlaceholder(FileAttributes attributes)
    {
        uint raw = (uint)attributes;
        return (attributes & FileAttributes.Offline) != 0 ||
               (raw & FileAttributeRecallOnOpen) != 0 ||
               (raw & FileAttributeRecallOnDataAccess) != 0;
    }

    internal static bool IsCloudReparseTag(uint reparseTag) =>
        (reparseTag & IoReparseTagCloudMask) == IoReparseTagCloudBase;

    internal static bool IsUnsafeReparsePoint(string fullPath, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        // Hydrated OneDrive / Windows Cloud Filter files carry a CLOUD* reparse
        // tag even when their full bytes live on disk. Allow only hydrated cloud
        // reparse points; symlinks, junctions, and unknown tags stay blocked.
        return !TryGetReparseTag(fullPath, out uint reparseTag) ||
               !IsCloudReparseTag(reparseTag);
    }

    internal static bool HasUnsafeSourceAttributes(string fullPath, FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary)) != 0)
        {
            return true;
        }

        if (IsOfflineOrCloudPlaceholder(attributes))
        {
            return true;
        }

        return IsUnsafeReparsePoint(fullPath, attributes);
    }

    private static bool IsHiddenOrSystemEntry(
        string fullPath,
        string name,
        FileAttributes attributes,
        bool isDirectory)
    {
        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            return true;
        }

        if (IgnoredSystemFileNames.Any(ignored =>
                name.Equals(ignored, StringComparison.OrdinalIgnoreCase)) ||
            name.StartsWith("._", StringComparison.Ordinal))
        {
            return true;
        }

        return !isDirectory && IsOwnedManagedStorageShortcut(fullPath, name);
    }

    private static bool IsOwnedManagedStorageShortcut(string fullPath, string name)
    {
        if (!name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? configuredShortcutPath = global::MagneticDesk.App.Current?.SettingsService?.Settings
            .ManagedStorageDesktopShortcutPath;
        if (!string.IsNullOrWhiteSpace(configuredShortcutPath))
        {
            try
            {
                if (string.Equals(
                        Path.GetFullPath(configuredShortcutPath),
                        fullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        string basePrefix = Path.GetFileNameWithoutExtension(
            ManagedStorageDesktopShortcutService.ShortcutFileName);
        if (!name.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            ShortcutInfo? metadata = ShortcutHelper.ReadStoredMetadata(fullPath);
            return metadata is not null &&
                   string.Equals(
                       metadata.Description,
                       ManagedStorageDesktopShortcutService.ShortcutDescription,
                       StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasTemporaryOrLockName(string name) =>
        TemporarySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ||
        name.StartsWith("~$", StringComparison.Ordinal) ||
        name.StartsWith(".~lock.", StringComparison.OrdinalIgnoreCase);

    private static bool HasActiveDownloadSidecar(string fullPath) =>
        ActiveDownloadSidecarSuffixes.Any(suffix => File.Exists(fullPath + suffix));

    private static bool TryGetReparseTag(string fullPath, out uint reparseTag)
    {
        reparseTag = 0;
        IntPtr handle = FindFirstFileW(fullPath, out Win32FindData findData);
        if (handle == InvalidHandleValue)
        {
            return false;
        }

        try
        {
            if ((findData.dwFileAttributes & (uint)FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }

            reparseTag = findData.dwReserved0;
            return true;
        }
        finally
        {
            FindClose(handle);
        }
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out Win32FindData lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr hFindFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindData
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    private static string NormalizeOptionalPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);

    private static bool IsUnderPath(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(
            $"{normalizedParent}{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
    }
}
