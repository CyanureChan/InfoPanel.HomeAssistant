using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Current state of a Home Assistant entity from the REST API.</summary>
public sealed class HomeAssistantEntityState
{
    /// <summary>Full entity id (e.g. sensor.temperature).</summary>
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Current state value (e.g. 21.5, on, unavailable).</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = "unavailable";

    /// <summary>Optional entity attributes from Home Assistant.</summary>
    [JsonPropertyName("attributes")]
    public HomeAssistantAttributes? Attributes { get; set; }

    /// <summary>Friendly name from attributes, or the entity id when unset.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Attributes?.FriendlyName) ? EntityId : Attributes!.FriendlyName!;
}
