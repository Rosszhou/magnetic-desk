using System.Security.Cryptography;
using System.Text.Json;

namespace MagneticDesk.Uninstaller;

public sealed record PayloadFile(string Path, string Sha256);
public sealed record UninstallManifest(string Product, PayloadFile[] Files)
{
    public const string FileName = "magnetic-desk.UninstallManifest.json";

    public static UninstallManifest Load(string root)
    {
        root = System.IO.Path.GetFullPath(root);
        if (System.IO.Path.GetPathRoot(root)?.TrimEnd('\\') == root.TrimEnd('\\'))
            throw new InvalidDataException("A drive root cannot be uninstalled.");
        EnsureNoLinks(System.IO.Path.GetPathRoot(root)!, root);
        var manifest = JsonSerializer.Deserialize<UninstallManifest>(
            File.ReadAllText(System.IO.Path.Combine(root, FileName)))
            ?? throw new InvalidDataException("Missing uninstall manifest.");
        if (manifest.Product != "magnetic-desk" || manifest.Files is null || manifest.Files.Length == 0)
            throw new InvalidDataException("Invalid product manifest.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            Resolve(root, entry.Path);
            if (!unique.Add(entry.Path.Replace('/', '\\')) || entry.Sha256.Length != 64 ||
                entry.Sha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid file identity.");
        }
        if (!unique.Contains("magnetic-desk.exe") || !unique.Contains("magnetic-desk.Uninstaller.exe"))
            throw new InvalidDataException("Application files are missing from the manifest.");
        return manifest;
    }

    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Split('/', '\\').Any(p => p is ".." or "." or ""))
            throw new InvalidDataException("Invalid uninstall path.");
        root = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Uninstall path escaped the application directory.");
        EnsureNoLinks(root, path);
        return path;
    }

    private static void EnsureNoLinks(string root, string path)
    {
        for (string? current = path; current is not null; current = System.IO.Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked directories and files cannot be uninstalled.");
            if (string.Equals(current.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
