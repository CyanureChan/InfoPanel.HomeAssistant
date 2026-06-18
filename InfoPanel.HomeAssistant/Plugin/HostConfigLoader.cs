using System.Text.Json;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin;

internal static class HostConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void ApplyStoredConfig(string pluginId, Action<string, object?> applyConfig)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InfoPanel",
            "plugins",
            $"{pluginId}.config.json");

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                File.ReadAllText(path),
                JsonOptions);

            if (stored == null)
            {
                return;
            }

            foreach (var (key, element) in stored)
            {
                applyConfig(key, CoerceJsonElement(element));
            }
        }
        catch
        {
            // Host will retry applying config after Initialize; ignore parse errors here.
        }
    }

    private static object? CoerceJsonElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out int i) => i,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.GetString() ?? element.ToString(),
        };
}
