using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Models;
using InfoPanel.HomeAssistant.Core.Services;
using InfoPanel.HomeAssistant.Plugin.Layout;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin;

/// <summary>InfoPanel plugin entry point for Home Assistant integration.</summary>
public sealed class HomeAssistantPlugin : BasePlugin, IPluginConfigurable
{
    private readonly HomeAssistantRuntime _runtime = new();
    private readonly PluginText _connectionStatus =
        new("connection-status", "Connection Status", "Not configured");

    private DiscoveredPluginLayout _layout = DiscoveredPluginLayout.Empty;
    private bool _discoverySettingsDirty;
    private int _pollCycleCount;
    private bool _loggedLegacyUpdateMode;
    private bool _loggedLegacyCatalogRefresh;
    private string _lastStableStatus = string.Empty;

    private string _baseUrl = string.Empty;
    private string _accessToken = string.Empty;
    private string _entityDomains = HomeAssistantSettings.DefaultEntityDomains;
    private string _entityInclude = string.Empty;
    private string _entityExclude = string.Empty;
    private int _maxEntities = HomeAssistantSettings.DefaultMaxEntities;
    private int _pollIntervalSeconds = HomeAssistantSettings.DefaultPollIntervalSeconds;

    public HomeAssistantPlugin()
        : base(
            "home-assistant-plugin",
            "Home Assistant",
            "Home Assistant integration with entity discovery. Version: 0.6.2")
    {
    }

    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(
        Math.Clamp(_pollIntervalSeconds, HomeAssistantSettings.MinPollIntervalSeconds, HomeAssistantSettings.MaxPollIntervalSeconds));

    public IReadOnlyList<PluginConfigProperty> ConfigProperties =>
    [
        new PluginConfigProperty
        {
            Key = "BaseUrl",
            DisplayName = "Base URL",
            Description = "Home Assistant URL without a trailing slash.",
            Type = PluginConfigType.String,
            Value = _baseUrl
        },
        new PluginConfigProperty
        {
            Key = "AccessToken",
            DisplayName = "Access Token",
            Description = "Long-Lived Access Token from your Home Assistant profile.",
            Type = PluginConfigType.String,
            Value = _accessToken
        },
        new PluginConfigProperty
        {
            Key = "PollIntervalSeconds",
            DisplayName = "Refresh Interval (seconds)",
            Description =
                "How often dirty entity values push to InfoPanel. WebSocket keeps HA data live between refreshes.",
            Type = PluginConfigType.Integer,
            Value = _pollIntervalSeconds,
            MinValue = HomeAssistantSettings.MinPollIntervalSeconds,
            MaxValue = HomeAssistantSettings.MaxPollIntervalSeconds,
            Step = 1
        },
        new PluginConfigProperty
        {
            Key = "EntityInclude",
            DisplayName = "Entity Include",
            Description =
                "Comma-separated rules (entity/device/integration/domain/*). Combined with Entity Domains. " +
                "entity/device/integration rules always include all matching entities. Exclusions override includes. " +
                "Use * for all supported types (uncapped). Reload after changes.",
            Type = PluginConfigType.String,
            Value = _entityInclude
        },
        new PluginConfigProperty
        {
            Key = "EntityDomains",
            DisplayName = "Entity Domains",
            Description =
                "Always combined with Entity Include. Comma-separated domains or * for all supported types " +
                "(uncapped). Default: sensor,climate,binary_sensor.",
            Type = PluginConfigType.String,
            Value = _entityDomains
        },
        new PluginConfigProperty
        {
            Key = "EntityExclude",
            DisplayName = "Entity Exclude",
            Description =
                "Comma-separated rules with same syntax as Include. Exclusions are absolute and override all includes. " +
                "Example: light.room, domain.input_boolean, integration.backup.",
            Type = PluginConfigType.String,
            Value = _entityExclude
        },
        new PluginConfigProperty
        {
            Key = "MaxEntities",
            DisplayName = "Max Entities",
            Description =
                "Safeguard cap for domain/general pool only (default 2000). Use 0 or * in Include/Domains for unlimited. " +
                "entity/device/integration rules are never capped.",
            Type = PluginConfigType.Integer,
            Value = _maxEntities,
            MinValue = 0,
            MaxValue = HomeAssistantSettings.MaxMaxEntities,
            Step = 1
        }
    ];

    public void ApplyConfig(string key, object? value)
    {
        if (!TryApplyConfigValue(key, value, markDiscoveryDirty: true))
        {
            return;
        }

        HomeAssistantPluginEngine.ApplySettings(_runtime, BuildSettings());

        if (_discoverySettingsDirty && _runtime.IsConfigured)
        {
            SetConnectionStatus("Config updated - click Reload to refresh entity list", force: true);
        }
    }

    public override void Initialize()
    {
        // Config is applied by the host after Initialize(); discovery runs in Load().
    }

    public override void Load(List<IPluginContainer> containers)
    {
        HostConfigLoader.ApplyStoredConfig(Id, (key, value) => TryApplyConfigValue(key, value, markDiscoveryDirty: true));
        HomeAssistantPluginEngine.ApplySettings(_runtime, BuildSettings());
        _discoverySettingsDirty = false;
        _pollCycleCount = 0;
        _lastStableStatus = string.Empty;

        if (!_runtime.IsConfigured)
        {
            SetConnectionStatus("Not configured", force: true);
            _layout = DiscoveredPluginLayout.Empty;
        }
        else
        {
            TryDiscoverLayout();
        }

        HomeAssistantPluginEngine.RegisterLayout(containers, _layout, _connectionStatus);
    }

    public override async Task UpdateAsync(CancellationToken cancellationToken)
    {
        if (!_runtime.IsConfigured)
        {
            SetConnectionStatus("Not configured", force: true);
            return;
        }

        if (_layout.AllEntries.Count == 0)
        {
            SetConnectionStatus(await DescribeEmptyDiscoveryAsync(cancellationToken), force: true);
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _pollCycleCount++;
        bool forceFullSync = _pollCycleCount % HomeAssistantSettings.FullSyncPollInterval == 0;

        try
        {
            StateUpdateBatch batch = await _runtime.PrepareUpdateBatchAsync(forceFullSync, cancellationToken);
            int changed = 0;

            if (batch.States.Count > 0)
            {
                changed = HomeAssistantPluginEngine.ApplyStates(_layout.EntryByEntityId, batch.States);
                _runtime.ClearAppliedDirty(batch.States.Select(s => s.EntityId));
            }

            UpdateStableConnectionStatus();
            HomeAssistantPluginTelemetry.RecordPoll(batch.Skipped && changed == 0, changed, stopwatch.ElapsedMilliseconds);
            HomeAssistantPluginTelemetry.MaybeLogSummary();

            HomeAssistantPluginLog.Info(
                $"Update: {batch.FetchPath}, WS={_runtime.IsStateStreamConnected}, dirty={batch.DirtyCount}, " +
                $"applied={batch.States.Count}, changed={changed}, poll_ms={stopwatch.ElapsedMilliseconds}, " +
                $"skipped={batch.Skipped && batch.States.Count == 0}");
        }
        catch (Exception ex)
        {
            SetConnectionStatus(ex.Message, force: true);
        }
    }

    public override void Update() =>
        UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override void Close()
    {
        _runtime.Dispose();
        _layout = DiscoveredPluginLayout.Empty;
    }

    private bool TryApplyConfigValue(string key, object? value, bool markDiscoveryDirty)
    {
        switch (key)
        {
            case "BaseUrl":
                _baseUrl = value?.ToString() ?? string.Empty;
                if (markDiscoveryDirty)
                {
                    _discoverySettingsDirty = true;
                }

                return true;
            case "AccessToken":
                _accessToken = value?.ToString() ?? string.Empty;
                if (markDiscoveryDirty)
                {
                    _discoverySettingsDirty = true;
                }

                return true;
            case "UpdateMode":
                if (!_loggedLegacyUpdateMode)
                {
                    _loggedLegacyUpdateMode = true;
                    HomeAssistantPluginLog.Info(
                        "UpdateMode config ignored (WebSocket with automatic REST fallback).");
                }

                return true;
            case "CatalogRefreshSeconds":
                if (!_loggedLegacyCatalogRefresh)
                {
                    _loggedLegacyCatalogRefresh = true;
                    HomeAssistantPluginLog.Info(
                        "CatalogRefreshSeconds removed in v0.6.1; use Refresh Interval only.");
                }

                return true;
            case "PollIntervalSeconds":
                _pollIntervalSeconds = PluginConfigCoercion.ToInt(
                    value,
                    HomeAssistantSettings.DefaultPollIntervalSeconds);
                return true;
            case "EntityInclude":
                _entityInclude = value?.ToString() ?? string.Empty;
                if (markDiscoveryDirty)
                {
                    _discoverySettingsDirty = true;
                }

                return true;
            case "EntityDomains":
                _entityDomains = string.IsNullOrWhiteSpace(value?.ToString())
                    ? HomeAssistantSettings.DefaultEntityDomains
                    : value!.ToString()!;
                if (markDiscoveryDirty)
                {
                    _discoverySettingsDirty = true;
                }

                return true;
            case "EntityExclude":
                _entityExclude = value?.ToString() ?? string.Empty;
                if (markDiscoveryDirty)
                {
                    _discoverySettingsDirty = true;
                }

                return true;
            case "MaxEntities":
                _maxEntities = PluginConfigCoercion.ToInt(value, HomeAssistantSettings.DefaultMaxEntities);
                if (markDiscoveryDirty)
                {
                    _discoverySettingsDirty = true;
                }

                return true;
            default:
                return false;
        }
    }

    private void TryDiscoverLayout()
    {
        var settings = BuildSettings();
        HomeAssistantPluginLog.Info(
            $"Discovery using include='{settings.EntityInclude}', domains='{settings.EntityDomains}', " +
            $"exclude='{settings.EntityExclude}', maxEntities={settings.MaxEntities}.");

        try
        {
            _layout = HomeAssistantPluginEngine
                .DiscoverLayoutAsync(_runtime, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (_layout.AllEntries.Count > 0)
            {
                UpdateStableConnectionStatus(force: true);
            }
            else
            {
                SetConnectionStatus("No entities matched filters", force: true);
            }

            if (!string.IsNullOrWhiteSpace(_layout.RegistryWarning))
            {
                SetConnectionStatus($"{_connectionStatus.Value} ({_layout.RegistryWarning})", force: true);
            }
        }
        catch (Exception ex)
        {
            _layout = DiscoveredPluginLayout.Empty;
            HomeAssistantPluginLog.Error(ex, "Discovery failed during Load.");
            SetConnectionStatus($"Discovery failed: {ex.Message} — click Reload", force: true);
        }
    }

    private void UpdateStableConnectionStatus(bool force = false)
    {
        string status = BuildStableStatus();
        SetConnectionStatus(status, force);
    }

    private string BuildStableStatus()
    {
        int entityCount = _layout.AllEntries.Count;
        int groupCount = _layout.Groups.Count;
        var status = $"OK ({entityCount} entities: {_layout.ExplicitCount} explicit + {_layout.PoolCount} from domains";

        if (_layout.PoolMatchedBeforeCap > _layout.PoolCount)
        {
            status += $", {_layout.PoolMatchedBeforeCap} domain matches before cap";
        }

        if (groupCount > 0)
        {
            status += $", {groupCount} devices";
        }

        status += $", {_runtime.SubscriptionMode}, {_pollIntervalSeconds}s refresh";
        status += _runtime.IsStateStreamConnected ? ", WS connected" : ", WS reconnecting";

        return status + ")";
    }

    private void SetConnectionStatus(string value, bool force = false)
    {
        if (!force && string.Equals(_lastStableStatus, value, StringComparison.Ordinal))
        {
            return;
        }

        _lastStableStatus = value;
        _connectionStatus.Value = value;
    }

    private async Task<string> DescribeEmptyDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (_discoverySettingsDirty)
        {
            return "Config updated - click Reload to refresh entity list";
        }

        try
        {
            var discovery = await _runtime.DiscoverAsync(cancellationToken);
            if (discovery.SelectedStates.Count == 0)
            {
                int total = (await _runtime.FetchAllStatesAsync(cancellationToken)).Count;
                return total == 0
                    ? "Connected but Home Assistant returned no entities"
                    : $"No entities matched filters ({total} total in HA)";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return "No entities matched filters - click Reload after changing config";
    }

    private HomeAssistantSettings BuildSettings() => new()
    {
        BaseUrl = _baseUrl,
        AccessToken = _accessToken,
        EntityDomains = _entityDomains,
        EntityInclude = _entityInclude,
        EntityExclude = _entityExclude,
        MaxEntities = _maxEntities,
        PollIntervalSeconds = _pollIntervalSeconds,
    };
}
