namespace InfoPanel.HomeAssistant.Plugin.Layout;

/// <summary>Full discovery result mapped to plugin containers and entries.</summary>
internal sealed class DiscoveredPluginLayout
{
    private static readonly IReadOnlyDictionary<string, EntityEntry> EmptyLookup =
        new Dictionary<string, EntityEntry>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Empty layout used before discovery or after close.</summary>
    public static DiscoveredPluginLayout Empty { get; } = new()
    {
        Groups = [],
        AllEntries = [],
        EntryByEntityId = EmptyLookup,
    };

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

    /// <summary>Flat list of all entity entries, cached at discovery.</summary>
    public required IReadOnlyList<EntityEntry> AllEntries { get; init; }

    /// <summary>Entity id to entry map for O(dirty) state apply.</summary>
    public required IReadOnlyDictionary<string, EntityEntry> EntryByEntityId { get; init; }
}
