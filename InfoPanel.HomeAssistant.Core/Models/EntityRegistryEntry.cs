using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Single row from the Home Assistant entity registry.</summary>
public sealed class EntityRegistryEntry
{
    /// <summary>Full entity id (e.g. sensor.temperature).</summary>
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Linked device registry id, when assigned.</summary>
    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    /// <summary>Integration platform that owns the entity (e.g. mqtt, bambu_lab).</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
}
