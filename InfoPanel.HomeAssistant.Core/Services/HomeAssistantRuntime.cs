using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Mapping;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

public sealed class HomeAssistantRuntime : IDisposable
{
    private readonly HomeAssistantApiService _api = new();
    private readonly HomeAssistantRegistryClient _registryClient = new();

    public HomeAssistantSettings Settings { get; } = new();
    public HomeAssistantRegistrySnapshot Registry { get; private set; } = HomeAssistantRegistrySnapshot.Unavailable();
    public IReadOnlyList<string> SelectedEntityIds { get; private set; } = [];

    public bool IsConfigured => _api.IsConfigured;

    public void ApplySettings(HomeAssistantSettings settings)
    {
        Settings.BaseUrl = settings.BaseUrl;
        Settings.AccessToken = settings.AccessToken;
        Settings.EntityDomains = settings.EntityDomains;
        Settings.EntityInclude = settings.EntityInclude;
        Settings.MaxEntities = settings.MaxEntities;
        _api.Configure(Settings.BaseUrl, Settings.AccessToken);
    }

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        Registry = await _registryClient.FetchAsync(Settings.BaseUrl, Settings.AccessToken, cancellationToken);
        var allStates = await _api.GetAllStatesAsync(cancellationToken);
        int supportedCount = allStates.Count(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId));
        var (selected, matchedBeforeCap) = EntityDiscovery.Filter(allStates, Settings, Registry);
        SelectedEntityIds = selected.Select(s => s.EntityId).ToList();

        var groups = EntityContainerGrouper.Group(selected, Registry);
        string? registryWarning = Registry.IsAvailable
            ? null
            : $"Registry unavailable - grouped by fallback ({Registry.ErrorMessage ?? "unknown"})";

        return new DiscoveryResult
        {
            Groups = groups,
            SelectedStates = selected,
            MatchedBeforeCap = matchedBeforeCap,
            SupportedEntityCount = supportedCount,
            RegistryAvailable = Registry.IsAvailable,
            RegistryWarning = registryWarning,
        };
    }

    public async Task<IReadOnlyList<HomeAssistantEntityState>> DiscoverEntitiesAsync(
        CancellationToken cancellationToken)
    {
        var result = await DiscoverAsync(cancellationToken);
        return result.SelectedStates;
    }

    public async Task<IReadOnlyList<HomeAssistantEntityState>> FetchAllStatesAsync(
        CancellationToken cancellationToken) =>
        await _api.GetAllStatesAsync(cancellationToken);

    public async Task<IReadOnlyList<HomeAssistantEntityState>> FetchFilteredStatesAsync(
        CancellationToken cancellationToken)
    {
        if (SelectedEntityIds.Count == 0)
        {
            return [];
        }

        var selected = SelectedEntityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = await _api.GetAllStatesAsync(cancellationToken);
        return all
            .Where(s => selected.Contains(s.EntityId))
            .ToList();
    }

    public void Dispose() => _api.Dispose();
}
