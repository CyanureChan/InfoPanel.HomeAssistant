using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Mapping;
using InfoPanel.HomeAssistant.Core.Models;
using InfoPanel.HomeAssistant.Core.Services;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin;

internal sealed class EntityEntry
{
    public required string EntityId { get; init; }
    public required IPluginData Data { get; init; }
    public PluginSensor? Sensor { get; init; }
    public PluginText? Text { get; init; }

    public static EntityEntry Create(HomeAssistantEntityState state)
    {
        string entryId = EntityIdHelper.ToEntryId(state.EntityId);
        string displayName = state.DisplayName;

        if (ExposableEntityDomains.IsTextDomain(state.EntityId))
        {
            var text = new PluginText(entryId, displayName, "-");
            return new EntityEntry
            {
                EntityId = state.EntityId,
                Data = text,
                Text = text,
            };
        }

        string? unit = state.Attributes?.UnitOfMeasurement;
        var sensor = new PluginSensor(entryId, displayName, 0, unit);
        return new EntityEntry
        {
            EntityId = state.EntityId,
            Data = sensor,
            Sensor = sensor,
        };
    }

    public void ApplyState(HomeAssistantEntityState state)
    {
        if (EntityStateMapper.IsUnavailable(state.State))
        {
            if (Text != null)
            {
                Text.Value = state.State;
            }

            return;
        }

        if (Sensor != null)
        {
            if (EntityStateMapper.TryParseNumeric(state.State, out float value))
            {
                Sensor.Value = value;
            }

            return;
        }

        if (Text != null)
        {
            Text.Value = state.State;
        }
    }
}

internal sealed class DiscoveredPluginLayout
{
    public required IReadOnlyList<EntityGroupLayout> Groups { get; init; }
    public string? RegistryWarning { get; init; }
    public int MatchedBeforeCap { get; init; }
    public int SupportedEntityCount { get; init; }

    public IReadOnlyList<EntityEntry> AllEntries =>
        Groups.SelectMany(g => g.Entries).ToList();
}

internal sealed class EntityGroupLayout
{
    public required string ContainerId { get; init; }
    public required string ContainerName { get; init; }
    public required IReadOnlyList<EntityEntry> Entries { get; init; }
}

internal static class HomeAssistantPluginEngine
{
    public const string SystemContainerId = "system";

    public static void ApplySettings(HomeAssistantRuntime runtime, HomeAssistantSettings settings) =>
        runtime.ApplySettings(settings);

    public static async Task<DiscoveredPluginLayout> DiscoverLayoutAsync(
        HomeAssistantRuntime runtime,
        CancellationToken cancellationToken)
    {
        DiscoveryResult discovery = await runtime.DiscoverAsync(cancellationToken);
        var groups = discovery.Groups
            .Select(group => new EntityGroupLayout
            {
                ContainerId = group.ContainerId,
                ContainerName = group.ContainerName,
                Entries = group.States.Select(EntityEntry.Create).ToList(),
            })
            .ToList();

        return new DiscoveredPluginLayout
        {
            Groups = groups,
            RegistryWarning = discovery.RegistryWarning,
            MatchedBeforeCap = discovery.MatchedBeforeCap,
            SupportedEntityCount = discovery.SupportedEntityCount,
        };
    }

    public static void RegisterLayout(
        List<IPluginContainer> containers,
        DiscoveredPluginLayout layout,
        PluginText connectionStatus)
    {
        var systemContainer = new PluginContainer(SystemContainerId, "System");
        systemContainer.Entries.Add(connectionStatus);
        containers.Add(systemContainer);

        foreach (var group in layout.Groups)
        {
            var container = new PluginContainer(group.ContainerId, group.ContainerName);
            RegisterEntries(container, group.Entries);
            containers.Add(container);
        }
    }

    public static void RegisterEntries(IPluginContainer container, IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            container.Entries.Add(entry.Data);
        }
    }

    public static void UpdateEntries(IEnumerable<EntityEntry> entries, IEnumerable<HomeAssistantEntityState> states)
    {
        var byId = states.ToDictionary(s => s.EntityId, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (byId.TryGetValue(entry.EntityId, out var state))
            {
                entry.ApplyState(state);
            }
        }
    }
}
