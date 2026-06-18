using System.Text.Json.Serialization;

namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Single row from the Home Assistant device registry.</summary>
public sealed class DeviceRegistryEntry
{
    /// <summary>Device registry uuid.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Manufacturer or integration-provided device name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>User-renamed device label, when set.</summary>
    [JsonPropertyName("name_by_user")]
    public string? NameByUser { get; set; }

    /// <summary>Preferred display name: user label, then integration name, then id.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(NameByUser) ? Name ?? Id : NameByUser!;
}
