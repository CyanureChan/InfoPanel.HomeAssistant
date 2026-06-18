namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Outcome of a full entity discovery pass against Home Assistant.</summary>
public sealed class DiscoveryResult
{
    /// <summary>Entities grouped into InfoPanel containers.</summary>
    public required IReadOnlyList<EntityGroup> Groups { get; init; }

    /// <summary>Flat list of selected entity states.</summary>
    public required IReadOnlyList<HomeAssistantEntityState> SelectedStates { get; init; }

    /// <summary>Count of entities matched by explicit-tier rules after exclusions.</summary>
    public int ExplicitCount { get; init; }

    /// <summary>Count of entities matched by the domain pool after cap and exclusions.</summary>
    public int PoolCount { get; init; }

    /// <summary>Domain pool matches before the Max Entities cap is applied.</summary>
    public int PoolMatchedBeforeCap { get; init; }

    /// <summary>Total matches before the domain pool cap.</summary>
    public int MatchedBeforeCap { get; init; }

    /// <summary>Supported entities present in Home Assistant regardless of filters.</summary>
    public int SupportedEntityCount { get; init; }

    /// <summary>Whether the WebSocket entity/device registry was available.</summary>
    public bool RegistryAvailable { get; init; }

    /// <summary>Warning when registry data could not be loaded.</summary>
    public string? RegistryWarning { get; init; }
}
