using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

public sealed class HomeAssistantEntityState
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = "unavailable";

    [JsonPropertyName("attributes")]
    public HomeAssistantAttributes? Attributes { get; set; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Attributes?.FriendlyName) ? EntityId : Attributes!.FriendlyName!;
}

public sealed class HomeAssistantAttributes
{
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; set; }

    [JsonPropertyName("unit_of_measurement")]
    public string? UnitOfMeasurement { get; set; }
}
