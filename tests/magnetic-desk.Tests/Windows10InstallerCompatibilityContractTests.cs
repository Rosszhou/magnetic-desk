namespace MagneticDesk.Tests;

public sealed class Windows10InstallerCompatibilityContractTests
{
    [Theory]
    [InlineData("installer/magnetic-desk.iss")]
    [InlineData("installer/magnetic-desk.arm64.iss")]
    public void DirectInstaller_MatchesPackagedWindows10Minimum(string scriptPath)
    {
        string installer = File.ReadAllText(TestPaths.FromRepository(scriptPath));
        string manifest = File.ReadAllText(TestPaths.FromRepository(
            "src/magnetic-desk/Package.appxmanifest"));

        Assert.Contains("MinVersion=10.0.19041", installer, StringComparison.Ordinal);
        Assert.Contains("MinVersion=\"10.0.19041.0\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows10Floor_IsDeclaredConsistentlyAcrossProjectsAndProbes()
    {
        // 19041 (Windows 10 2004) is the base build of the 19041-19045 servicing family;
        // every declared floor must move together when the compatibility floor changes.
        foreach (string projectPath in new[]
                 {
                     "src/magnetic-desk/magnetic-desk.csproj",
                     "src/magnetic-desk.Updater/magnetic-desk.Updater.csproj",
                     "tests/magnetic-desk.Tests/magnetic-desk.Tests.csproj",
                     "tests/magnetic-desk.NotificationActivationProbe/magnetic-desk.NotificationActivationProbe.csproj"
                 })
        {
            string project = File.ReadAllText(TestPaths.FromRepository(projectPath));
            Assert.Contains(
                "<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>",
                project,
                StringComparison.Ordinal);
        }

        string mainProject = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/magnetic-desk.csproj"));
        Assert.Contains(
            "<TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>",
            mainProject,
            StringComparison.Ordinal);

        string startupProbe = File.ReadAllText(TestPaths.FromRepository(
            "scripts/Test-StoreStartupTaskLifecycle.ps1"));
        Assert.Contains("MinVersion=\"10.0.19041.0\"", startupProbe, StringComparison.Ordinal);

        string notificationProbe = File.ReadAllText(TestPaths.FromRepository(
            "tests/magnetic-desk.NotificationActivationProbe/Run-MSIX.ps1"));
        Assert.Contains("MinVersion=\"10.0.19041.0\"", notificationProbe, StringComparison.Ordinal);
    }
}
