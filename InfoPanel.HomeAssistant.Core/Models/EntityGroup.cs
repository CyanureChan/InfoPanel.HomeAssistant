namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>A group of Home Assistant entities displayed under one InfoPanel container.</summary>
public sealed class EntityGroup
{
    /// <summary>URL-safe container id used in binding paths.</summary>
    public required string ContainerId { get; init; }

    /// <summary>Human-readable container label (e.g. Bambu Lab.A1MINI).</summary>
    public required string ContainerName { get; init; }

    /// <summary>Entity states belonging to this container.</summary>
    public required IReadOnlyList<HomeAssistantEntityState> States { get; init; }
}
