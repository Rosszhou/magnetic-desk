using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class MagneticDeskDataPathIsolationTests
{
    [Fact]
    public void ProductionRoot_PreservesLegacyInstanceNamesAndRecoveryLocation()
    {
        var service = new MagneticDeskDataPathService();

        Assert.False(service.IsDevelopmentRoot);
        Assert.Equal("MagneticDesk_Activate_Event_7F3A9B2E", service.ActivationEventName);
        Assert.Equal("MagneticDesk_SingleInstance_Mutex_7F3A9B2E", service.SingleInstanceMutexName);
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(service.RootPath)!, "magnetic-desk-Recovery"),
            service.RecoveryDirectory);
    }

    [Fact]
    public void DevelopmentRoot_IsolatesDataRecoveryAndInstanceNames()
    {
        string firstRoot = Path.Combine(Path.GetTempPath(), "MagneticDesk-Dev-QuickCapture139");
        string secondRoot = Path.Combine(Path.GetTempPath(), "MagneticDesk-Dev-Todo139");
        var first = new MagneticDeskDataPathService(firstRoot);
        var same = new MagneticDeskDataPathService(firstRoot.ToLowerInvariant());
        var second = new MagneticDeskDataPathService(secondRoot);

        Assert.True(first.IsDevelopmentRoot);
        Assert.Equal($"{first.RootPath}-Recovery", first.RecoveryDirectory);
        Assert.StartsWith(first.RootPath, first.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first.SingleInstanceMutexName, same.SingleInstanceMutexName);
        Assert.NotEqual(first.SingleInstanceMutexName, second.SingleInstanceMutexName);
        Assert.NotEqual("MagneticDesk_SingleInstance_Mutex_7F3A9B2E", first.SingleInstanceMutexName);
    }

    [Fact]
    public void DebugEnvironmentVariable_IsPartOfTheStartupContract()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/magnetic-desk/Services/MagneticDeskDataPathService.cs"));

        Assert.Contains("MAGNETIC_DESK_DEV_DATA_ROOT", source, StringComparison.Ordinal);
        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("ResolveConfiguredRoot", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/magnetic-desk/Services/SettingsService.cs")]
    [InlineData("src/magnetic-desk/Services/QuickCaptureStore.cs")]
    [InlineData("src/magnetic-desk/Services/TodoWidgetStore.cs")]
    [InlineData("src/magnetic-desk/Services/SearchHistoryService.cs")]
    [InlineData("src/magnetic-desk/Services/LegacySearchIndexCleanupService.cs")]
    [InlineData("src/magnetic-desk/Services/DesktopOrganizationRecoveryStore.cs")]
    public void AppOwnedStorage_UsesSharedDataRoot(string relativePath)
    {
        string source = File.ReadAllText(TestPaths.FromRepository(relativePath));

        Assert.Contains("MagneticDeskDataPathService.Current", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.SpecialFolder.LocalApplicationData", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugLauncher_DefaultsToAWorktreeScopedDataRoot()
    {
        string source = File.ReadAllText(TestPaths.FromRepository("scripts/start-debug.ps1"));

        Assert.Contains("MagneticDesk-Dev", source, StringComparison.Ordinal);
        Assert.Contains("MAGNETIC_DESK_DEV_DATA_ROOT", source, StringComparison.Ordinal);
        Assert.Contains("UseProductionData", source, StringComparison.Ordinal);
        Assert.Contains("ExecutablePath.StartsWith($repoRootPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TestHosts_NeverConstructTheProductionPathOrganizerService()
    {
        // The public OrganizerService ctor hardcodes the production
        // suppression ledger and recovery journal; a test using it silently
        // reads and rewrites the real data directory and races the running
        // app. History: unisolated tests once grew the ledger to 8.4 MB and
        // left 27 quarantined corrupt backups. Every test must go through
        // TestOrganizerServices (or the internal ctor with injected paths).
        string testsRoot = TestPaths.FromRepository("tests/magnetic-desk.Tests");
        // Assembled from fragments so this test's own source does not carry
        // the literal it bans.
        string bannedConstruction = "new " + "OrganizerService(";
        foreach (string file in Directory.EnumerateFiles(
                     testsRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                Path.GetFileName(file) is "TestOrganizerServices.cs")
            {
                continue;
            }

            string source = File.ReadAllText(file);
            Assert.False(
                source.Contains(bannedConstruction, StringComparison.Ordinal),
                $"{file} constructs OrganizerService directly; use " +
                "TestOrganizerServices.Create so the suppression ledger and " +
                "recovery journal stay off the real data directory.");
        }
    }
}
