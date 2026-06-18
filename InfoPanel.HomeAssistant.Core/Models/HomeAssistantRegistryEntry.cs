using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

public sealed class EntityRegistryEntry
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
}

public sealed class DeviceRegistryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("name_by_user")]
    public string? NameByUser { get; set; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(NameByUser) ? Name ?? Id : NameByUser!;
}

public sealed class HomeAssistantRegistrySnapshot
{
    public static HomeAssistantRegistrySnapshot Unavailable(string? error = null) => new()
    {
        IsAvailable = false,
        ErrorMessage = error,
    };

    public bool IsAvailable { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<EntityRegistryEntry> Entities { get; init; } = [];
    public IReadOnlyList<DeviceRegistryEntry> Devices { get; init; } = [];

    private Dictionary<string, EntityRegistryEntry>? _entitiesById;
    private Dictionary<string, DeviceRegistryEntry>? _devicesById;

    public IReadOnlyDictionary<string, EntityRegistryEntry> EntitiesById =>
        _entitiesById ??= Entities.ToDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, DeviceRegistryEntry> DevicesById =>
        _devicesById ??= Devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
}
