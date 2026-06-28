using System.Text.Json;
using InfoPanel.HomeAssistant.Core.Services;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin;

internal static class HostConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void ApplyStoredConfig(string pluginId, Action<string, object?> applyConfigValue)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InfoPanel",
            "plugins",
            $"{pluginId}.config.json");

        if (!File.Exists(path))
        {
            HomeAssistantPluginLog.Warn($"No stored config at {path}");
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                File.ReadAllText(path),
                JsonOptions);

            if (stored == null)
            {
                HomeAssistantPluginLog.Warn($"Stored config at {path} was empty.");
                return;
            }

            HomeAssistantPluginLog.Info($"Loading stored config from {path} ({stored.Count} keys).");

            foreach (var (key, element) in stored)
            {
                if (string.Equals(key, "UpdateMode", StringComparison.OrdinalIgnoreCase))
                {
                    HomeAssistantPluginLog.Info(
                        "Stored UpdateMode ignored (WebSocket with automatic REST fallback).");
                    continue;
                }

                if (string.Equals(key, "CatalogRefreshSeconds", StringComparison.OrdinalIgnoreCase))
                {
                    HomeAssistantPluginLog.Info(
                        "Stored CatalogRefreshSeconds ignored in v0.6.1; use Refresh Interval only.");
                    continue;
                }

                if (string.Equals(key, "AccessToken", StringComparison.OrdinalIgnoreCase))
                {
                    HomeAssistantPluginLog.Debug("Config key AccessToken: (redacted)");
                    applyConfigValue(key, CoerceJsonElement(element));
                    continue;
                }

                HomeAssistantPluginLog.Debug($"Config key {key}: {element}");
                applyConfigValue(key, CoerceJsonElement(element));
            }
        }
        catch (Exception ex)
        {
            HomeAssistantPluginLog.Error(ex, $"Failed to read stored config at {path}");
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
