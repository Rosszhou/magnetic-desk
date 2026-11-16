using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using MagneticDesk.Uninstaller;

internal static class Program
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string title, uint flags);

    [STAThread]
    private static int Main(string[] args)
    {
        bool quiet = args.Contains("--quiet");
        try
        {
            int rootIndex = Array.IndexOf(args, "--root");
            string root = Path.GetFullPath(rootIndex >= 0 ? args[rootIndex + 1] : AppContext.BaseDirectory).TrimEnd('\\');
            var manifest = UninstallManifest.Load(root);
            string helper = Path.Combine(root, "magnetic-desk.Uninstaller.exe");
            if (!manifest.Files.Any(f => f.Path.Equals("magnetic-desk.Uninstaller.exe", StringComparison.OrdinalIgnoreCase) &&
                UninstallManifest.Hash(helper).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The uninstaller does not match the file manifest.");
            if (args.Contains("--verify")) return 0;
            if (args.Contains("--register"))
            {
                Register(root);
                return 0;
            }
            if (!args.Contains("--execute"))
            {
                if (MessageBoxW(0, "卸载 magnetic-desk？\n\n将关闭此目录运行的软件并清理程序文件与自启动。\n格子中的文件、便签、待办、设置和恢复快照会保留。\n\nUninstall this copy? Application files and startup entries will be removed. Your files and data will be kept.\n\n目录 / Folder: " + root,
                    "magnetic-desk 卸载 / Uninstall", 0x124) != 6) return 0;
                string temporary = Path.Combine(Path.GetTempPath(), "magnetic-desk-uninstall-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                string detached = Path.Combine(temporary, Path.GetFileName(helper));
                File.Copy(helper, detached);
                var start = new ProcessStartInfo(detached) { UseShellExecute = false };
                foreach (string argument in new[] { "--execute", "--root", root }) start.ArgumentList.Add(argument);
                _ = Process.Start(start) ?? throw new IOException("Unable to start the detached uninstaller.");
                return 0;
            }
            // All paths are validated before stopping the application or changing files.
            string executable = Path.Combine(root, "magnetic-desk.exe");
            foreach (var process in new[] { "magnetic-desk", "magnetic-desk.ThumbnailProxy", "magnetic-desk.Updater" }
                .SelectMany(Process.GetProcessesByName))
            {
                using (process)
                {
                    string? processPath = process.MainModule?.FileName;
                    if (!string.Equals(Path.GetDirectoryName(processPath), root, StringComparison.OrdinalIgnoreCase)) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(5000); }
                }
            }
            CleanupStartup(executable);
            int preserved = 0;
            var failures = new List<string>();
            foreach (var entry in manifest.Files)
            {
                if (entry.Path.Equals("magnetic-desk.Uninstaller.exe", StringComparison.OrdinalIgnoreCase)) continue;
                string path = UninstallManifest.Resolve(root, entry.Path);
                if (!File.Exists(path)) continue;
                try
                {
                    if (!UninstallManifest.Hash(path).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    { preserved++; continue; }
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { failures.Add(entry.Path + " — " + ex.Message); }
            }
            if (failures.Count > 0)
                throw new IOException("部分程序文件未能删除，可稍后重试。\n" + string.Join("\n", failures.Take(5)));
            File.Delete(helper);
            File.Delete(Path.Combine(root, UninstallManifest.FileName));
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
                key?.DeleteSubKeyTree(RegistrationName(root), false);
            var directories = manifest.Files.Select(f => Path.GetDirectoryName(UninstallManifest.Resolve(root, f.Path)))
                .Where(p => p is not null).SelectMany(p => Parents(root, p!)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(p => p.Length);
            foreach (string directory in directories)
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (!quiet) MessageBoxW(0, "程序已卸载。用户数据与文件已保留。\nUninstalled. Your files and data were preserved.\n" +
                (preserved > 0 ? "已保留修改过的程序文件：" + preserved : ""), "magnetic-desk", 0x40);
            ScheduleTemporaryCleanup();
            return 0;
        }
        catch (Exception ex)
        {
            if (!quiet) MessageBoxW(0, ex.Message, "magnetic-desk 卸载失败 / Uninstall failed", 0x10);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static IEnumerable<string> Parents(string root, string directory)
    {
        while (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            yield return directory;
            if (directory.Equals(root, StringComparison.OrdinalIgnoreCase)) yield break;
            directory = Path.GetDirectoryName(directory)!;
        }
    }

    private static void ScheduleTemporaryCleanup()
    {
        string? executable = Environment.ProcessPath;
        if (executable is null) return;
        string? directory = Path.GetDirectoryName(executable);
        if (directory is null || Path.GetDirectoryName(directory)?.TrimEnd('\\') != Path.GetTempPath().TrimEnd('\\') ||
            !Path.GetFileName(directory).StartsWith("magnetic-desk-uninstall-", StringComparison.Ordinal)) return;
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.Environment["MAGNETIC_DESK_CLEANUP_PID"] = Environment.ProcessId.ToString();
        start.Environment["MAGNETIC_DESK_CLEANUP_EXE"] = executable;
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "Wait-Process -Id ([int]$env:MAGNETIC_DESK_CLEANUP_PID) -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $env:MAGNETIC_DESK_CLEANUP_EXE -ErrorAction SilentlyContinue; $dir=Split-Path -Parent $env:MAGNETIC_DESK_CLEANUP_EXE; if (@(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) { Remove-Item -LiteralPath $dir -ErrorAction SilentlyContinue }" }) start.ArgumentList.Add(arg);
        _ = Process.Start(start);
    }

    private static string RegistrationName(string root) => "magnetic-desk.Portable." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];

    private static void Register(string root)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + RegistrationName(root));
        key.SetValue("DisplayName", "magnetic-desk");
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("Publisher", "magnetic-desk");
        key.SetValue("InstallLocation", root);
        key.SetValue("DisplayIcon", Path.Combine(root, "Assets", "magnetic-desk.ico"));
        key.SetValue("UninstallString", "\"" + Path.Combine(root, "magnetic-desk.Uninstaller.exe") + "\"");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
    }

    private static bool OwnsCommand(string? command, string executable)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        command = command.Trim();
        string target = command.StartsWith('"') ? command[1..].Split('"')[0] : command.Split(' ')[0];
        return string.Equals(target, executable, StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupStartup(string executable)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            if (key is not null)
                foreach (string name in key.GetValueNames())
                    if (OwnsCommand(key.GetValue(name) as string, executable)) key.DeleteValue(name, false);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.Environment["MAGNETIC_DESK_REMOVE_EXE"] = executable;
        start.Environment["PSModulePath"] = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; Get-ScheduledTask -TaskName 'MagneticDesk User Startup*' -ErrorAction SilentlyContinue | ForEach-Object { if (@($_.Actions | Where-Object { $_.Execute.Trim('\"') -ieq $env:MAGNETIC_DESK_REMOVE_EXE }).Count -gt 0) { $_ | Unregister-ScheduledTask -Confirm:$false } }" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Unable to inspect startup tasks.");
        if (!process.WaitForExit(30000)) { process.Kill(); throw new IOException("Startup cleanup timed out."); }
        if (process.ExitCode != 0) throw new IOException("Unable to clean startup tasks. Retry with permission to manage this installation.");
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            foreach (string directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                string shortcut = Path.Combine(directory, "magnetic-desk.lnk");
                if (!File.Exists(shortcut)) continue;
                dynamic link = shell.CreateShortcut(shortcut);
                try { if (string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase)) File.Delete(shortcut); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
