using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Selected attributes from a Home Assistant entity state payload.</summary>
public sealed class HomeAssistantAttributes
{
    /// <summary>User-visible entity name.</summary>
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; set; }

    /// <summary>Unit suffix for numeric sensors (e.g. °C, %).</summary>
    [JsonPropertyName("unit_of_measurement")]
    public string? UnitOfMeasurement { get; set; }
}
