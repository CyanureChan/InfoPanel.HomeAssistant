namespace InfoPanel.HomeAssistant.Core.Configuration;

/// <summary>Parses update mode values from plugin configuration.</summary>
public static class StateUpdateModeParser
{
    public const string WebSocket = "WebSocket";
    public const string HttpPoll = "HttpPoll";
    public const string Hybrid = "Hybrid";

    public static readonly IReadOnlyList<string> Options = [WebSocket, HttpPoll, Hybrid];

    public static StateUpdateMode Parse(string? value) =>
        value?.Trim() switch
        {
            HttpPoll => StateUpdateMode.HttpPoll,
            Hybrid => StateUpdateMode.Hybrid,
            _ => StateUpdateMode.WebSocket,
        };

    public static string ToConfigValue(StateUpdateMode mode) =>
        mode switch
        {
            StateUpdateMode.HttpPoll => HttpPoll,
            StateUpdateMode.Hybrid => Hybrid,
            _ => WebSocket,
        };
}
