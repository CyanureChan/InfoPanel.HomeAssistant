using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Compressed entity state from Home Assistant subscribe_entities WebSocket API.</summary>
public sealed class HomeAssistantCompressedEntityState
{
    /// <summary>Current state value.</summary>
    [JsonPropertyName("s")]
    public string? State { get; set; }

    /// <summary>Entity attributes.</summary>
    [JsonPropertyName("a")]
    public Dictionary<string, JsonElement>? Attributes { get; set; }

    /// <summary>Context id or object.</summary>
    [JsonPropertyName("c")]
    public JsonElement? Context { get; set; }

    /// <summary>Last changed unix timestamp.</summary>
    [JsonPropertyName("lc")]
    public double? LastChanged { get; set; }

    /// <summary>Last updated unix timestamp.</summary>
    [JsonPropertyName("lu")]
    public double? LastUpdated { get; set; }
}
