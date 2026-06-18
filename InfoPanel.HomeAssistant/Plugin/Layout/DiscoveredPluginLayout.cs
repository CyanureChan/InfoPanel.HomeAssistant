namespace InfoPanel.HomeAssistant.Plugin.Layout;

/// <summary>Full discovery result mapped to plugin containers and entries.</summary>
internal sealed class DiscoveredPluginLayout
{
    /// <summary>Discovered entity groups excluding the system container.</summary>
    public required IReadOnlyList<EntityGroupLayout> Groups { get; init; }

    /// <summary>Registry warning to surface in connection status.</summary>
    public string? RegistryWarning { get; init; }

    /// <summary>Entities matched by explicit-tier rules.</summary>
    public int ExplicitCount { get; init; }

    /// <summary>Entities matched by the domain pool after cap.</summary>
    public int PoolCount { get; init; }

    /// <summary>Domain pool matches before cap.</summary>
    public int PoolMatchedBeforeCap { get; init; }

    /// <summary>Supported entities in Home Assistant.</summary>
    public int SupportedEntityCount { get; init; }

    /// <summary>All entity entries across every group.</summary>
    public IReadOnlyList<EntityEntry> AllEntries =>
        Groups.SelectMany(g => g.Entries).ToList();
}
