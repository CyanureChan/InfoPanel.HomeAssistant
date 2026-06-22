namespace InfoPanel.HomeAssistant.Core.Configuration;

/// <summary>How entity state values are kept in sync with Home Assistant.</summary>
public enum StateUpdateMode
{
    /// <summary>Live updates via WebSocket; poll interval refreshes InfoPanel only.</summary>
    WebSocket,

    /// <summary>Periodic full REST fetch of all HA states (legacy behavior).</summary>
    HttpPoll,

    /// <summary>WebSocket plus periodic per-entity REST sync at the poll interval.</summary>
    Hybrid,
}
