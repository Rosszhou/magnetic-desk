namespace MagneticDesk.Services;

/// <summary>
/// Independently toggleable cloud-backup domains (roadmap §10). Each flag
/// owns a disjoint set of data-directory paths; the widget-style domain
/// carries no files and is delivered as a projection document instead.
/// </summary>
[Flags]
public enum BackupArchiveDomain
{
    None = 0,
    TodoData = 1,
    QuickCaptureData = 2,
    WidgetStyle = 4
}

/// <summary>
/// Single source of truth for which data-relative paths belong to which
/// cloud-backup domain, used by both the scoped export filter and the
/// scoped restore overlay so they can never disagree.
///
/// Path ownership rules (see roadmap §10):
///   TodoData         — data/widgets/&lt;id&gt;/todo.json only. The widget's
///                      attachments/ subtree is deliberately NOT in the
///                      domain for now: uploads have no size bound, so one
///                      large attached file would ship whole. Excluding at
///                      domain level (not export-filter level) also keeps
///                      restore semantics right — the snapshot-faithful
///                      delete phase only touches in-domain files, so a
///                      restore never wipes the user's live attachments.
///                      todo.json still carries attachment FilePaths; they
///                      simply dangle on a machine that lacks the files.
///   QuickCaptureData — data/quick-capture/quick-capture.json only;
///                      attachments/ excluded for the same reason
///                      (thumbnails/ and exports/ are derived artifacts and
///                      stay out either way).
///   WidgetStyle      — no data files; carried as widget-style.json.
///
/// Everything else — settings.json, widget-layout.json (device-local layout
/// state), FileSafety history/journal, sidecars, caches, device.id,
/// file-widget contents (which only ever hold path references anyway) — is
/// never part of a scoped cloud backup.
/// </summary>
internal static class BackupArchiveDomains
{
    internal const string BackupKind = "cloud-backup";
    internal const string WidgetStyleEntryName = "widget-style.json";

    internal static readonly BackupArchiveDomain[] FileDomains =
    [
        BackupArchiveDomain.TodoData,
        BackupArchiveDomain.QuickCaptureData
    ];

    internal static bool IsInDomain(BackupArchiveDomain domain, string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return domain switch
        {
            BackupArchiveDomain.TodoData => IsTodoDataPath(normalized),
            BackupArchiveDomain.QuickCaptureData => IsQuickCaptureDataPath(normalized),
            // WidgetStyle owns no data-directory files.
            _ => false
        };
    }

    /// <summary>
    /// True when <paramref name="relativePath"/> belongs to ANY enabled
    /// domain in <paramref name="scope"/> — the export-time filter.
    /// </summary>
    internal static bool IsInScope(BackupArchiveDomain scope, string relativePath)
    {
        foreach (BackupArchiveDomain domain in FileDomains)
        {
            if (scope.HasFlag(domain) && IsInDomain(domain, relativePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Manifest wire name for a domain (stable, additive).</summary>
    internal static string ToManifestName(BackupArchiveDomain domain) => domain switch
    {
        BackupArchiveDomain.TodoData => "todo-data",
        BackupArchiveDomain.QuickCaptureData => "quick-capture-data",
        BackupArchiveDomain.WidgetStyle => "widget-style",
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, null)
    };

    internal static bool TryFromManifestName(string name, out BackupArchiveDomain domain)
    {
        domain = name switch
        {
            "todo-data" => BackupArchiveDomain.TodoData,
            "quick-capture-data" => BackupArchiveDomain.QuickCaptureData,
            "widget-style" => BackupArchiveDomain.WidgetStyle,
            _ => BackupArchiveDomain.None
        };
        return domain != BackupArchiveDomain.None;
    }

    internal static string[] ToManifestNames(BackupArchiveDomain scope)
    {
        var names = new List<string>(3);
        foreach (BackupArchiveDomain domain in Enum.GetValues<BackupArchiveDomain>())
        {
            if (domain != BackupArchiveDomain.None && scope.HasFlag(domain))
            {
                names.Add(ToManifestName(domain));
            }
        }

        return names.ToArray();
    }

    internal static BackupArchiveDomain FromManifestNames(IEnumerable<string>? names)
    {
        var scope = BackupArchiveDomain.None;
        if (names is null)
        {
            return scope;
        }

        foreach (string name in names)
        {
            if (TryFromManifestName(name, out BackupArchiveDomain domain))
            {
                scope |= domain;
            }
        }

        return scope;
    }

    /// <summary>
    /// Extracts the widget id from a TodoData path ("widgets/&lt;id&gt;/todo.json"),
    /// or null when the path is not in the todo domain.
    /// </summary>
    internal static string? TryGetTodoWidgetId(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        const string prefix = "widgets/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = normalized[prefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        string tail = rest[(slash + 1)..];
        return tail.Equals("todo.json", StringComparison.OrdinalIgnoreCase)
            ? rest[..slash]
            : null;
    }

    // widgets/<id>/todo.json — the todo store, keyed off the widget dir.
    // attachments/ stays out of the domain until upload size bounds land.
    private static bool IsTodoDataPath(string normalizedPath)
    {
        const string prefix = "widgets/";
        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = normalizedPath[prefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        string tail = rest[(slash + 1)..];
        return tail.Equals("todo.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuickCaptureDataPath(string normalizedPath) =>
        normalizedPath.Equals("quick-capture/quick-capture.json", StringComparison.OrdinalIgnoreCase);
}
