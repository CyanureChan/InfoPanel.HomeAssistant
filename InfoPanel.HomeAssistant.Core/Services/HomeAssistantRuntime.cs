using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Mapping;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Coordinates API access, registry fetch, entity selection, and grouping.</summary>
public sealed class HomeAssistantRuntime : IDisposable
{
    private readonly HomeAssistantApiService _api = new();
    private readonly HomeAssistantRegistryClient _registryClient = new();

    /// <summary>Live settings mirror updated by <see cref="ApplySettings"/>.</summary>
    public HomeAssistantSettings Settings { get; } = new();

    /// <summary>Latest entity/device registry snapshot.</summary>
    public HomeAssistantRegistrySnapshot Registry { get; private set; } = HomeAssistantRegistrySnapshot.Unavailable();

    /// <summary>Entity ids selected during the last discovery pass.</summary>
    public IReadOnlyList<string> SelectedEntityIds { get; private set; } = [];

    /// <summary>True when URL and token are configured.</summary>
    public bool IsConfigured => _api.IsConfigured;

    /// <summary>Updates connection settings and reconfigures the API client.</summary>
    /// <param name="settings">Settings to apply.</param>
    public void ApplySettings(HomeAssistantSettings settings)
    {
        Settings.BaseUrl = settings.BaseUrl;
        Settings.AccessToken = settings.AccessToken;
        Settings.EntityDomains = settings.EntityDomains;
        Settings.EntityInclude = settings.EntityInclude;
        Settings.EntityExclude = settings.EntityExclude;
        Settings.MaxEntities = settings.MaxEntities;
        _api.Configure(Settings.BaseUrl, Settings.AccessToken);
    }

    /// <summary>
    /// Fetches registry data, selects entities, and groups them into containers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovery result with groups, counts, and registry status.</returns>
    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        Registry = await _registryClient.FetchAsync(Settings.BaseUrl, Settings.AccessToken, cancellationToken);
        var allStates = await _api.GetAllStatesAsync(cancellationToken);
        int supportedCount = allStates.Count(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId));
        EntitySelectionResult selection = EntityDiscovery.Filter(allStates, Settings, Registry);
        SelectedEntityIds = selection.Selected.Select(s => s.EntityId).ToList();

        var groups = EntityContainerGrouper.Group(selection.Selected, Registry);
        string? registryWarning = Registry.IsAvailable
            ? null
            : $"Registry unavailable - grouped by fallback ({Registry.ErrorMessage ?? "unknown"})";

        return new DiscoveryResult
        {
            Groups = groups,
            SelectedStates = selection.Selected,
            ExplicitCount = selection.ExplicitCount,
            PoolCount = selection.PoolCount,
            PoolMatchedBeforeCap = selection.PoolMatchedBeforeCap,
            MatchedBeforeCap = selection.MatchedBeforeCap,
            SupportedEntityCount = supportedCount,
            RegistryAvailable = Registry.IsAvailable,
            RegistryWarning = registryWarning,
        };
    }

    /// <summary>Runs discovery and returns only the selected entity states.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<HomeAssistantEntityState>> DiscoverEntitiesAsync(
        CancellationToken cancellationToken)
    {
        var result = await DiscoverAsync(cancellationToken);
        return result.SelectedStates;
    }

    /// <summary>Fetches all entity states from Home Assistant without filtering.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<HomeAssistantEntityState>> FetchAllStatesAsync(
        CancellationToken cancellationToken) =>
        await _api.GetAllStatesAsync(cancellationToken);

    /// <summary>
    /// Fetches states for entities selected during the last discovery pass.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    /// <summary>Releases the HTTP client.</summary>
    public void Dispose() => _api.Dispose();
}
