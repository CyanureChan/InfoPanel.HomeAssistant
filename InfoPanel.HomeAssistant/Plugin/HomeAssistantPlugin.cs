using InfoPanel.HomeAssistant.Core.Configuration;
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

    private DiscoveredPluginLayout _layout = new() { Groups = [] };
    private bool _discoverySettingsDirty;

    private string _baseUrl = string.Empty;
    private string _accessToken = string.Empty;
    private string _entityDomains = HomeAssistantSettings.DefaultEntityDomains;
    private string _entityInclude = string.Empty;
    private string _entityExclude = string.Empty;
    private int _maxEntities = HomeAssistantSettings.DefaultMaxEntities;
    private int _pollIntervalSeconds = HomeAssistantSettings.DefaultPollIntervalSeconds;
    private string _updateMode = StateUpdateModeParser.WebSocket;

    public HomeAssistantPlugin()
        : base(
            "home-assistant-plugin",
            "Home Assistant",
            "Home Assistant integration with entity discovery. Version: 0.6.0")
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
            Key = "UpdateMode",
            DisplayName = "Update Mode",
            Description =
                "WebSocket: live HA push (default). HttpPoll: full REST fetch each interval. " +
                "Hybrid: WebSocket plus per-entity REST sync each interval.",
            Type = PluginConfigType.Choice,
            Value = _updateMode,
            Options = StateUpdateModeParser.Options.ToArray()
        },
        new PluginConfigProperty
        {
            Key = "PollIntervalSeconds",
            DisplayName = "Poll Interval (seconds)",
            Description =
                "Seconds between plugin update cycles. WebSocket mode uses this for InfoPanel refresh only; " +
                "HttpPoll mode controls REST fetch frequency.",
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
                "Cap for domain/general pool only (default 50). Use 0 or * in Include/Domains for unlimited. " +
                "entity/device/integration rules are never capped.",
            Type = PluginConfigType.Integer,
            Value = _maxEntities,
            MinValue = 0,
            MaxValue = 500,
            Step = 1
        }
    ];

    public void ApplyConfig(string key, object? value)
    {
        switch (key)
        {
            case "BaseUrl":
                _baseUrl = value?.ToString() ?? string.Empty;
                _discoverySettingsDirty = true;
                break;
            case "AccessToken":
                _accessToken = value?.ToString() ?? string.Empty;
                _discoverySettingsDirty = true;
                break;
            case "UpdateMode":
                _updateMode = string.IsNullOrWhiteSpace(value?.ToString())
                    ? StateUpdateModeParser.WebSocket
                    : value!.ToString()!;
                break;
            case "PollIntervalSeconds":
                if (value is int intValue)
                {
                    _pollIntervalSeconds = intValue;
                }
                else if (int.TryParse(value?.ToString(), out int parsed))
                {
                    _pollIntervalSeconds = parsed;
                }
                break;
            case "EntityInclude":
                _entityInclude = value?.ToString() ?? string.Empty;
                _discoverySettingsDirty = true;
                break;
            case "EntityDomains":
                _entityDomains = string.IsNullOrWhiteSpace(value?.ToString())
                    ? HomeAssistantSettings.DefaultEntityDomains
                    : value!.ToString()!;
                _discoverySettingsDirty = true;
                break;
            case "EntityExclude":
                _entityExclude = value?.ToString() ?? string.Empty;
                _discoverySettingsDirty = true;
                break;
            case "MaxEntities":
                if (value is int maxValue)
                {
                    _maxEntities = maxValue;
                }
                else if (int.TryParse(value?.ToString(), out int parsedMax))
                {
                    _maxEntities = parsedMax;
                }
                _discoverySettingsDirty = true;
                break;
            default:
                return;
        }

        HomeAssistantPluginEngine.ApplySettings(_runtime, BuildSettings());

        if (_discoverySettingsDirty && _runtime.IsConfigured)
        {
            _connectionStatus.Value = "Config updated - click Reload to refresh entity list";
        }
    }

    public override void Initialize()
    {
        // Config is applied by the host after Initialize(); discovery runs in Load().
    }

    public override void Load(List<IPluginContainer> containers)
    {
        HostConfigLoader.ApplyStoredConfig(Id, ApplyConfig);
        HomeAssistantPluginEngine.ApplySettings(_runtime, BuildSettings());
        _discoverySettingsDirty = false;

        if (!_runtime.IsConfigured)
        {
            _connectionStatus.Value = "Not configured";
            _layout = new DiscoveredPluginLayout { Groups = [] };
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
            _connectionStatus.Value = "Not configured";
            return;
        }

        var entries = _layout.AllEntries;
        if (entries.Count == 0)
        {
            _connectionStatus.Value = await DescribeEmptyDiscoveryAsync(cancellationToken);
            return;
        }

        try
        {
            var states = await _runtime.FetchFilteredStatesAsync(cancellationToken);
            HomeAssistantPluginEngine.UpdateEntries(entries, states);
            _connectionStatus.Value = BuildOkStatus(entries.Count);
            HomeAssistantPluginLog.Info(
                $"Update: {_runtime.DescribeFetchPath()}, WS={_runtime.IsStateStreamConnected}, " +
                $"states={states.Count}, status={_connectionStatus.Value}");
        }
        catch (Exception ex)
        {
            _connectionStatus.Value = ex.Message;
        }
    }

    public override void Update() =>
        UpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override void Close()
    {
        _runtime.Dispose();
        _layout = new DiscoveredPluginLayout { Groups = [] };
    }

    private void TryDiscoverLayout()
    {
        try
        {
            _layout = HomeAssistantPluginEngine
                .DiscoverLayoutAsync(_runtime, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            int count = _layout.AllEntries.Count;
            if (count > 0)
            {
                _connectionStatus.Value = BuildOkStatus(count);
            }
            else
            {
                _connectionStatus.Value = "No entities matched filters";
            }

            if (!string.IsNullOrWhiteSpace(_layout.RegistryWarning))
            {
                _connectionStatus.Value = $"{_connectionStatus.Value} ({_layout.RegistryWarning})";
            }
        }
        catch (Exception ex)
        {
            _layout = new DiscoveredPluginLayout { Groups = [] };
            _connectionStatus.Value = ex.Message;
        }
    }

    private string BuildOkStatus(int entityCount)
    {
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

        status += $", {StateUpdateModeParser.ToConfigValue(BuildSettings().UpdateMode)}, {_pollIntervalSeconds}s";

        if (BuildSettings().UpdateMode != StateUpdateMode.HttpPoll)
        {
            status += _runtime.IsStateStreamConnected ? ", WS connected" : ", WS reconnecting";
        }

        return status + ")";
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
        UpdateMode = StateUpdateModeParser.Parse(_updateMode),
    };
}
