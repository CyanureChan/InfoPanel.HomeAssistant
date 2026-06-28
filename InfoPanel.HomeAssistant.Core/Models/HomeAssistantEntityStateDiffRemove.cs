using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Attribute keys removed from a compressed entity state diff.</summary>
public sealed class HomeAssistantEntityStateDiffRemove
{
    /// <summary>Attribute keys to remove.</summary>
    [JsonPropertyName("a")]
    public List<string>? Attributes { get; set; }
}
