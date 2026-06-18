using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Mapping;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Mapping;

public static class EntityContainerGrouper
{
    public static IReadOnlyList<EntityGroup> Group(
        IReadOnlyList<HomeAssistantEntityState> selectedStates,
        HomeAssistantRegistrySnapshot registry)
    {
        if (selectedStates.Count == 0)
        {
            return [];
        }

        var buckets = new Dictionary<string, List<HomeAssistantEntityState>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in selectedStates)
        {
            var (containerId, containerName) = ResolveContainer(state, registry);

            if (!buckets.TryGetValue(containerId, out var list))
            {
                list = [];
                buckets[containerId] = list;
                names[containerId] = containerName;
            }

            list.Add(state);
        }

        return buckets
            .OrderBy(kvp => names[kvp.Key], StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new EntityGroup
            {
                ContainerId = kvp.Key,
                ContainerName = names[kvp.Key],
                States = kvp.Value
                    .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();
    }

    private static (string ContainerId, string ContainerName) ResolveContainer(
        HomeAssistantEntityState state,
        HomeAssistantRegistrySnapshot registry)
    {
        if (registry.IsAvailable &&
            registry.EntitiesById.TryGetValue(state.EntityId, out var entityEntry) &&
            !string.IsNullOrWhiteSpace(entityEntry.DeviceId) &&
            registry.DevicesById.TryGetValue(entityEntry.DeviceId, out var device))
        {
            string integration = FormatIntegrationName(entityEntry.Platform);
            string deviceName = device.DisplayName;
            return (SlugHelper.ToContainerId(integration, deviceName), $"{integration}.{deviceName}");
        }

        return ResolveFallbackContainer(state);
    }

    private static (string ContainerId, string ContainerName) ResolveFallbackContainer(HomeAssistantEntityState state)
    {
        string[] parts = state.EntityId.Split('.', 3);
        string domain = parts.Length > 0 ? parts[0] : "unknown";
        string objectId = parts.Length > 1 ? parts[1] : state.EntityId;

        string prefix = objectId.Contains('_', StringComparison.Ordinal)
            ? objectId.Split('_')[0]
            : objectId;

        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = objectId;
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "unknown";
        }

        string integration = FormatIntegrationName(domain);
        string deviceName = prefix.Length == 1
            ? prefix.ToUpperInvariant()
            : char.ToUpperInvariant(prefix[0]) + prefix[1..];
        string containerName = $"{integration}.{deviceName}";
        return (SlugHelper.ToContainerId(integration, deviceName), containerName);
    }

    private static string FormatIntegrationName(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return "Unknown";
        }

        return platform.ToLowerInvariant() switch
        {
            "mqtt" => "MQTT",
            "zha" => "ZHA",
            "zwave" => "Z-Wave",
            "hue" => "Hue",
            "bambu_lab" => "Bambu Lab",
            _ when platform.Contains('_', StringComparison.Ordinal) =>
                string.Join(' ', platform.Split('_', StringSplitOptions.RemoveEmptyEntries)
                    .Select(static part => part.Length == 0
                        ? part
                        : char.ToUpperInvariant(part[0]) + part[1..])),
            _ when platform.Length == 1 => platform.ToUpperInvariant(),
            _ => char.ToUpperInvariant(platform[0]) + platform[1..],
        };
    }
}
