namespace InfoPanel.HomeAssistant.Core.Models;

public sealed class EntityGroup
{
    public required string ContainerId { get; init; }
    public required string ContainerName { get; init; }
    public required IReadOnlyList<HomeAssistantEntityState> States { get; init; }
}

public sealed class DiscoveryResult
{
    public required IReadOnlyList<EntityGroup> Groups { get; init; }
    public required IReadOnlyList<HomeAssistantEntityState> SelectedStates { get; init; }
    public int MatchedBeforeCap { get; init; }
    public int SupportedEntityCount { get; init; }
    public bool RegistryAvailable { get; init; }
    public string? RegistryWarning { get; init; }
}
