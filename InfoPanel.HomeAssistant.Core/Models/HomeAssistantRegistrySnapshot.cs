namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Entity and device registry data fetched over the Home Assistant WebSocket API.</summary>
public sealed class HomeAssistantRegistrySnapshot
{
    /// <summary>Creates a snapshot indicating registry data is unavailable.</summary>
    /// <param name="error">Optional error message from the fetch attempt.</param>
    public static HomeAssistantRegistrySnapshot Unavailable(string? error = null) => new()
    {
        IsAvailable = false,
        ErrorMessage = error,
    };

    /// <summary>True when registry lists were loaded successfully.</summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>Error detail when <see cref="IsAvailable"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>All entity registry rows.</summary>
    public IReadOnlyList<EntityRegistryEntry> Entities { get; init; } = [];

    /// <summary>All device registry rows.</summary>
    public IReadOnlyList<DeviceRegistryEntry> Devices { get; init; } = [];

    private Dictionary<string, EntityRegistryEntry>? _entitiesById;
    private Dictionary<string, DeviceRegistryEntry>? _devicesById;

    /// <summary>Entity registry rows indexed by entity id.</summary>
    public IReadOnlyDictionary<string, EntityRegistryEntry> EntitiesById =>
        _entitiesById ??= Entities.ToDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Device registry rows indexed by device id.</summary>
    public IReadOnlyDictionary<string, DeviceRegistryEntry> DevicesById =>
        _devicesById ??= Devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
}
