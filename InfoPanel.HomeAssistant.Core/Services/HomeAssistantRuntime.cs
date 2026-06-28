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

    /// <summary>Active WebSocket subscription mode.</summary>
    public string SubscriptionMode => _stateStream.SubscriptionMode;

    /// <summary>Dirty entity count in the WebSocket cache.</summary>
    public int DirtyCount => _stateStream.DirtyCount;

    /// <summary>Updates connection settings and reconfigures clients.</summary>
    public void ApplySettings(HomeAssistantSettings settings)
    {
        bool credentialsChanged =
            !string.Equals(Settings.BaseUrl, settings.BaseUrl, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Settings.AccessToken, settings.AccessToken, StringComparison.Ordinal);

        Settings.BaseUrl = settings.BaseUrl;
        Settings.AccessToken = settings.AccessToken;
        Settings.EntityDomains = settings.EntityDomains;
        Settings.EntityInclude = settings.EntityInclude;
        Settings.EntityExclude = settings.EntityExclude;
        Settings.MaxEntities = settings.MaxEntities;
        Settings.PollIntervalSeconds = settings.GetEffectivePollIntervalSeconds();
        _api.Configure(Settings.BaseUrl, Settings.AccessToken);

        HomeAssistantPluginLog.Info(
            $"Settings applied: refresh={Settings.GetEffectivePollIntervalSeconds()}s, " +
            $"entities cap={Settings.MaxEntities}, configured={IsConfigured}.");

        if (credentialsChanged)
        {
            _ = RestartStateStreamAsync(CancellationToken.None);
        }
    }

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        HomeAssistantPluginLog.Info(
            "Discovery started (registry + full REST states). " +
            $"include='{Settings.EntityInclude}', domains='{Settings.EntityDomains}', " +
            $"maxEntities={Settings.MaxEntities}.");
        Registry = await _registryClient.FetchAsync(Settings.BaseUrl, Settings.AccessToken, cancellationToken);
        var allStates = await _api.GetAllStatesAsync(cancellationToken);
        int supportedCount = allStates.Count(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId));
        EntitySelectionResult selection = EntityDiscovery.Filter(allStates, Settings, Registry);
        bool poolUncapped = selection.PoolMatchedBeforeCap <= selection.PoolCount ||
                            selection.PoolMatchedBeforeCap == 0;
        SelectedEntityIds = selection.Selected.Select(s => s.EntityId).ToList();

        ConfigureStateStream(selection.Selected);

        HomeAssistantPluginLog.Info(
            $"Discovery complete: {SelectedEntityIds.Count} selected " +
            $"({selection.ExplicitCount} explicit + {selection.PoolCount} pool, " +
            $"poolBeforeCap={selection.PoolMatchedBeforeCap}, uncapped={poolUncapped}), " +
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

    public async Task<IReadOnlyList<HomeAssistantEntityState>> FetchAllStatesAsync(
        CancellationToken cancellationToken) =>
        await _api.GetAllStatesAsync(cancellationToken);

    /// <summary>
    /// Returns dirty states for the current update cycle, with optional REST fallback when WS is down.
    /// </summary>
    public async Task<StateUpdateBatch> PrepareUpdateBatchAsync(
        bool forceFullSync,
        CancellationToken cancellationToken)
    {
        if (SelectedEntityIds.Count == 0)
        {
            return new StateUpdateBatch
            {
                States = [],
                FetchPath = "no entities selected",
                DirtyCount = 0,
                Skipped = true,
            };
        }

        if (forceFullSync)
        {
            _stateStream.MarkAllDirty();
        }

        if (_stateStream.IsConnected)
        {
            var dirty = _stateStream.GetDirtyStates();
            if (dirty.Count == 0)
            {
                return new StateUpdateBatch
                {
                    States = [],
                    FetchPath = $"WebSocket cache ({SubscriptionMode})",
                    DirtyCount = 0,
                    Skipped = true,
                };
            }

            return new StateUpdateBatch
            {
                States = dirty,
                FetchPath = $"WebSocket cache ({SubscriptionMode})",
                DirtyCount = dirty.Count,
                Skipped = false,
            };
        }

        var dirtyIds = _stateStream.GetDirtyEntityIds();
        if (dirtyIds.Count == 0)
        {
            return new StateUpdateBatch
            {
                States = [],
                FetchPath = "REST per-entity fallback",
                DirtyCount = 0,
                Skipped = true,
            };
        }

        HomeAssistantPluginLog.Warn(
            $"Fetch: WebSocket offline, per-entity REST for {dirtyIds.Count} dirty entities.");
        var states = await FetchViaPerEntityRestAsync(dirtyIds, cancellationToken);
        return new StateUpdateBatch
        {
            States = states,
            FetchPath = "REST per-entity fallback",
            DirtyCount = states.Count,
            Skipped = false,
        };
    }

    public void ClearAppliedDirty(IEnumerable<string> entityIds) =>
        _stateStream.ClearDirty(entityIds);

    public string DescribeFetchPath() =>
        _stateStream.IsConnected
            ? $"WebSocket cache ({SubscriptionMode})"
            : "REST per-entity fallback";

    private void ConfigureStateStream(IReadOnlyList<HomeAssistantEntityState> selectedStates)
    {
        _stateStream.SeedStates(selectedStates, markDirty: true);
        _stateStream.SetEntityIds(SelectedEntityIds);
        _ = RestartStateStreamAsync(CancellationToken.None);
    }

    private async Task RestartStateStreamAsync(CancellationToken cancellationToken)
    {
        _stateStream.Stop();

        if (!IsConfigured || SelectedEntityIds.Count == 0)
        {
            HomeAssistantPluginLog.Info("State stream not started (not configured or no entities).");
            return;
        }

        HomeAssistantPluginLog.Info("Starting WebSocket state stream.");
        await _stateStream.StartAsync(Settings.BaseUrl, Settings.AccessToken, cancellationToken);
    }

    private async Task<IReadOnlyList<HomeAssistantEntityState>> FetchViaPerEntityRestAsync(
        IReadOnlyList<string> dirtyEntityIds,
        CancellationToken cancellationToken)
    {
        var states = await _api.GetEntityStatesAsync(dirtyEntityIds, cancellationToken);
        _stateStream.SeedStates(states, markDirty: true);
        return states;
    }

    public void Dispose()
    {
        _stateStream.Stop();
        _api.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _stateStream.DisposeAsync();
        _api.Dispose();
    }
}
