namespace InfoPanel.HomeAssistant.Plugin.Layout;

/// <summary>One device/integration container and its entity entries after discovery.</summary>
internal sealed class EntityGroupLayout
{
    /// <summary>URL-safe container id.</summary>
    public required string ContainerId { get; init; }

    /// <summary>Display name shown in the InfoPanel sensor tree.</summary>
    public required string ContainerName { get; init; }

    /// <summary>Entity entries registered under this container.</summary>
    public required IReadOnlyList<EntityEntry> Entries { get; init; }
}
