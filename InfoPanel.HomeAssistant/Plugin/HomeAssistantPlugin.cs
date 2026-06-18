using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Services;
using InfoPanel.HomeAssistant.Plugin;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin;

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
    private int _maxEntities = HomeAssistantSettings.DefaultMaxEntities;

    public HomeAssistantPlugin()
        : base(
            "home-assistant-plugin",
            "Home Assistant",
            "Home Assistant integration with entity discovery. Version: 0.3.1")
    {
    }

    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(15);

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
            Key = "EntityInclude",
            DisplayName = "Entity Include",
            Description =
                "One rule per line. Examples: entity.sensor.temperature, device.A1MINI, integration.bambu_lab, domain.sensor, or * for all supported types. " +
                "When set, Entity Domains is ignored. Only sensor/binary_sensor/climate/number/switch/cover/lock types are exposed (not automation, script, button, etc.). Reload after changes.",
            Type = PluginConfigType.String,
            Value = _entityInclude
        },
        new PluginConfigProperty
        {
            Key = "EntityDomains",
            DisplayName = "Entity Domains",
            Description =
                "Used only when Entity Include is empty. Comma-separated domains or * for all supported types " +
                "(sensor, binary_sensor, climate, number, input_number, switch, input_boolean, cover, lock). Default: sensor,climate,binary_sensor.",
            Type = PluginConfigType.String,
            Value = _entityDomains
        },
        new PluginConfigProperty
        {
            Key = "MaxEntities",
            DisplayName = "Max Entities",
            Description =
                "Cap after filters apply. Results are sorted A–Z by entity id, so low values skip later sensors (e.g. sensor.a1mini_*). Raise this or use specific Entity Include rules.",
            Type = PluginConfigType.Integer,
            Value = _maxEntities,
            MinValue = 1,
            MaxValue = 200,
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
            case "MaxEntities":
                if (value is int intValue)
                {
                    _maxEntities = intValue;
                }
                else if (int.TryParse(value?.ToString(), out int parsed))
                {
                    _maxEntities = parsed;
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
        string baseStatus = groupCount > 0
            ? $"OK ({entityCount} entities in {groupCount} devices"
            : $"OK ({entityCount} entities";

        if (_layout.MatchedBeforeCap > entityCount)
        {
            baseStatus += $", {_layout.MatchedBeforeCap} matched - raise Max Entities";
        }

        baseStatus += ")";
        return baseStatus;
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
        MaxEntities = _maxEntities > 0 ? _maxEntities : HomeAssistantSettings.DefaultMaxEntities,
    };
}
