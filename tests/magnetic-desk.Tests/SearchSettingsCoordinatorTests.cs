using MagneticDesk.Contracts;
using MagneticDesk.Features.Search;
using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class SearchSettingsCoordinatorTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "magnetic-desk.Tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsService _settings;

    public SearchSettingsCoordinatorTests() => _settings = new SettingsService(_root);
    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PrunedSearchFeature_CannotBeEnabledAndNeverInitializesRuntime()
    {
        int starts = 0;
        var connection = new FakeConnection();
        using var coordinator = new SearchSettingsCoordinator(
            _settings,
            TestServices.CreateLocalizationService(),
            () => null,
            () => { starts++; return connection; },
            () => null,
            _ => { });

        FeatureWidgetSettings.SetEnabled(_settings.Settings, WidgetKind.Search, true);
        Assert.False(FeatureWidgetSettings.IsEnabled(_settings.Settings, WidgetKind.Search));
        Assert.False(coordinator.Read().FeatureEnabled);
        Assert.Equal(EverythingConnectionState.Unknown, (await coordinator.RefreshConnectionAsync(default)).State);
        coordinator.UpdatePreferences(new(EverythingEnabled: true));
        Assert.Equal(0, starts);
        Assert.Equal(0, connection.Probes);
    }

    public async Task DisposeAsync()
    {
        await _settings.FlushPendingSaveAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeConnection : ISearchConnectionClient
    {
        public int Probes { get; private set; }
        public EverythingConnectionSnapshot CurrentSnapshot { get; private set; } = EverythingConnectionSnapshot.Unknown;
        public event Action<EverythingConnectionSnapshot>? ConnectionChanged;
        public Task<EverythingConnectionSnapshot> RefreshConnectionAsync(bool allowIpcProbe, CancellationToken cancellationToken)
        {
            Probes++;
            ConnectionChanged?.Invoke(CurrentSnapshot);
            return Task.FromResult(CurrentSnapshot);
        }
        public Task UseAutomaticDetectionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> SetExecutablePathAsync(string path, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> LaunchEverythingAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
