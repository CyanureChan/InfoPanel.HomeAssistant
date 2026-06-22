using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Mapping;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Coordinates API access, registry fetch, entity selection, and grouping.</summary>
public sealed class HomeAssistantRuntime : IAsyncDisposable, IDisposable
{
    private readonly HomeAssistantApiService _api = new();
    private readonly HomeAssistantRegistryClient _registryClient = new();
    private readonly HomeAssistantStateStream _stateStream = new();

    /// <summary>Live settings mirror updated by <see cref="ApplySettings"/>.</summary>
    public HomeAssistantSettings Settings { get; } = new();

    /// <summary>Latest entity/device registry snapshot.</summary>
    public HomeAssistantRegistrySnapshot Registry { get; private set; } = HomeAssistantRegistrySnapshot.Unavailable();

    /// <summary>Entity ids selected during the last discovery pass.</summary>
    public IReadOnlyList<string> SelectedEntityIds { get; private set; } = [];

    /// <summary>True when URL and token are configured.</summary>
    public bool IsConfigured => _api.IsConfigured;

    /// <summary>True when the WebSocket state stream is connected.</summary>
    public bool IsStateStreamConnected => _stateStream.IsConnected;

    /// <summary>Last WebSocket stream error, if any.</summary>
    public string? StateStreamError => _stateStream.LastError;

    /// <summary>Updates connection settings and reconfigures clients.</summary>
    /// <param name="settings">Settings to apply.</param>
    public void ApplySettings(HomeAssistantSettings settings)
    {
        bool credentialsChanged =
            !string.Equals(Settings.BaseUrl, settings.BaseUrl, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Settings.AccessToken, settings.AccessToken, StringComparison.Ordinal);

        bool modeChanged = Settings.UpdateMode != settings.UpdateMode;

        Settings.BaseUrl = settings.BaseUrl;
        Settings.AccessToken = settings.AccessToken;
        Settings.EntityDomains = settings.EntityDomains;
        Settings.EntityInclude = settings.EntityInclude;
        Settings.EntityExclude = settings.EntityExclude;
        Settings.MaxEntities = settings.MaxEntities;
        Settings.PollIntervalSeconds = settings.GetEffectivePollIntervalSeconds();
        Settings.UpdateMode = settings.UpdateMode;
        _api.Configure(Settings.BaseUrl, Settings.AccessToken);

        HomeAssistantPluginLog.Info(
            $"Settings applied: mode={Settings.UpdateMode}, poll={Settings.GetEffectivePollIntervalSeconds()}s, " +
            $"entities cap={Settings.MaxEntities}, configured={IsConfigured}.");

        if (credentialsChanged || modeChanged)
        {
            _ = RestartStateStreamAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Fetches registry data, selects entities, and groups them into containers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovery result with groups, counts, and registry status.</returns>
    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        HomeAssistantPluginLog.Info("Discovery started (registry + full REST states).");
        Registry = await _registryClient.FetchAsync(Settings.BaseUrl, Settings.AccessToken, cancellationToken);
        var allStates = await _api.GetAllStatesAsync(cancellationToken);
        int supportedCount = allStates.Count(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId));
        EntitySelectionResult selection = EntityDiscovery.Filter(allStates, Settings, Registry);
        SelectedEntityIds = selection.Selected.Select(s => s.EntityId).ToList();

        ConfigureStateStream(selection.Selected);

        HomeAssistantPluginLog.Info(
            $"Discovery complete: {SelectedEntityIds.Count} selected " +
            $"({selection.ExplicitCount} explicit + {selection.PoolCount} pool), " +
            $"registry={(Registry.IsAvailable ? "ok" : "unavailable")}.");

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
    /// Returns current states for selected entities using the configured update mode.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<HomeAssistantEntityState>> FetchFilteredStatesAsync(
        CancellationToken cancellationToken)
    {
        if (SelectedEntityIds.Count == 0)
        {
            return [];
        }

        return Settings.UpdateMode switch
        {
            StateUpdateMode.HttpPoll => await FetchViaBulkRestAsync(cancellationToken),
            StateUpdateMode.Hybrid => await FetchViaHybridAsync(cancellationToken),
            _ => await FetchViaWebSocketAsync(cancellationToken),
        };
    }

    /// <summary>Describes the active fetch path for diagnostics.</summary>
    public string DescribeFetchPath()
    {
        if (SelectedEntityIds.Count == 0)
        {
            return "no entities selected";
        }

        return Settings.UpdateMode switch
        {
            StateUpdateMode.HttpPoll => "REST bulk GET /api/states",
            StateUpdateMode.Hybrid when _stateStream.IsConnected =>
                "Hybrid: WebSocket cache + per-entity REST sync",
            StateUpdateMode.Hybrid => "Hybrid fallback: per-entity REST",
            StateUpdateMode.WebSocket when _stateStream.IsConnected => "WebSocket cache (state_changed)",
            _ => "WebSocket fallback: per-entity REST",
        };
    }

    private void ConfigureStateStream(IReadOnlyList<HomeAssistantEntityState> selectedStates)
    {
        _stateStream.SeedStates(selectedStates);
        _stateStream.SetEntityIds(SelectedEntityIds);
        _ = RestartStateStreamAsync(CancellationToken.None);
    }

    private async Task RestartStateStreamAsync(CancellationToken cancellationToken)
    {
        _stateStream.Stop();

        if (!IsConfigured ||
            Settings.UpdateMode == StateUpdateMode.HttpPoll ||
            SelectedEntityIds.Count == 0)
        {
            HomeAssistantPluginLog.Info(
                Settings.UpdateMode == StateUpdateMode.HttpPoll
                    ? "State stream not started (HttpPoll mode)."
                    : "State stream not started (not configured or no entities).");
            return;
        }

        HomeAssistantPluginLog.Info($"Starting state stream for {Settings.UpdateMode} mode.");
        await _stateStream.StartAsync(Settings.BaseUrl, Settings.AccessToken, cancellationToken);
    }

    private async Task<IReadOnlyList<HomeAssistantEntityState>> FetchViaWebSocketAsync(
        CancellationToken cancellationToken)
    {
        if (_stateStream.IsConnected)
        {
            HomeAssistantPluginLog.Debug("Fetch: WebSocket cache.");
            return _stateStream.GetSelectedStates();
        }

        HomeAssistantPluginLog.Warn("Fetch: WebSocket offline, using per-entity REST.");
        return await FetchViaPerEntityRestAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<HomeAssistantEntityState>> FetchViaHybridAsync(
        CancellationToken cancellationToken)
    {
        if (_stateStream.IsConnected)
        {
            HomeAssistantPluginLog.Debug("Fetch: Hybrid per-entity REST sync.");
            var synced = await _api.GetEntityStatesAsync(SelectedEntityIds, cancellationToken);
            _stateStream.SeedStates(synced);
            return _stateStream.GetSelectedStates();
        }

        HomeAssistantPluginLog.Warn("Fetch: Hybrid fallback per-entity REST.");
        return await FetchViaPerEntityRestAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<HomeAssistantEntityState>> FetchViaBulkRestAsync(
        CancellationToken cancellationToken)
    {
        HomeAssistantPluginLog.Debug("Fetch: REST bulk GET /api/states.");
        var selected = SelectedEntityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = await _api.GetAllStatesAsync(cancellationToken);
        return all
            .Where(s => selected.Contains(s.EntityId))
            .ToList();
    }

    private async Task<IReadOnlyList<HomeAssistantEntityState>> FetchViaPerEntityRestAsync(
        CancellationToken cancellationToken)
    {
        HomeAssistantPluginLog.Debug($"Fetch: per-entity REST for {SelectedEntityIds.Count} entities.");
        var states = await _api.GetEntityStatesAsync(SelectedEntityIds, cancellationToken);
        _stateStream.SeedStates(states);
        return states;
    }

    /// <summary>Releases HTTP and WebSocket resources.</summary>
    public void Dispose()
    {
        _stateStream.Stop();
        _api.Dispose();
    }

    /// <summary>Asynchronously releases WebSocket resources.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stateStream.DisposeAsync();
        _api.Dispose();
    }
}
