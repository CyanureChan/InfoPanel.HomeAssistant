using InfoPanel.HomeAssistant.Core.Models;
using InfoPanel.HomeAssistant.Core.Services;
using InfoPanel.HomeAssistant.Plugin.Layout;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin;

/// <summary>Builds plugin containers from Home Assistant discovery results.</summary>
internal static class HomeAssistantPluginEngine
{
    /// <summary>Container id for connection status and other system entries.</summary>
    public const string SystemContainerId = "system";

    /// <summary>Applies connection and filter settings to the runtime.</summary>
    public static void ApplySettings(HomeAssistantRuntime runtime, Core.Configuration.HomeAssistantSettings settings) =>
        runtime.ApplySettings(settings);

    /// <summary>
    /// Discovers entities and maps them to plugin layout groups.
    /// </summary>
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

        var allEntries = groups.SelectMany(g => g.Entries).ToList();
        var entryByEntityId = BuildEntryMap(allEntries);

        ApplyInitialStates(entryByEntityId, discovery.SelectedStates);

        HomeAssistantPluginLog.Info(
            $"Layout built: {allEntries.Count} entries in {groups.Count} groups " +
            $"(discovery selected {discovery.SelectedStates.Count}).");

        return new DiscoveredPluginLayout
        {
            Groups = groups,
            AllEntries = allEntries,
            EntryByEntityId = entryByEntityId,
            RegistryWarning = discovery.RegistryWarning,
            ExplicitCount = discovery.ExplicitCount,
            PoolCount = discovery.PoolCount,
            PoolMatchedBeforeCap = discovery.PoolMatchedBeforeCap,
            SupportedEntityCount = discovery.SupportedEntityCount,
        };
    }

    /// <summary>
    /// Registers discovered groups and the system container with the plugin host.
    /// </summary>
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

    /// <summary>Adds entity entries to a plugin container.</summary>
    public static void RegisterEntries(IPluginContainer container, IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            container.Entries.Add(entry.Data);
        }
    }

    /// <summary>Applies dirty states to matching entries only.</summary>
    /// <returns>Number of entries whose displayed value changed.</returns>
    public static int ApplyStates(
        IReadOnlyDictionary<string, EntityEntry> entryByEntityId,
        IEnumerable<HomeAssistantEntityState> states)
    {
        int changed = 0;
        foreach (var state in states)
        {
            if (entryByEntityId.TryGetValue(state.EntityId, out var entry) &&
                entry.ApplyState(state))
            {
                changed++;
            }
        }

        return changed;
    }

    private static Dictionary<string, EntityEntry> BuildEntryMap(IReadOnlyList<EntityEntry> allEntries)
    {
        var map = new Dictionary<string, EntityEntry>(allEntries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allEntries)
        {
            if (map.TryGetValue(entry.EntityId, out var existing))
            {
                HomeAssistantPluginLog.Warn(
                    $"Duplicate entity id in layout: {entry.EntityId} " +
                    $"(keeping {existing.Data.Id}, skipping {entry.Data.Id}).");
                continue;
            }

            map[entry.EntityId] = entry;
        }

        return map;
    }

    private static void ApplyInitialStates(
        IReadOnlyDictionary<string, EntityEntry> entryByEntityId,
        IReadOnlyList<HomeAssistantEntityState> states)
    {
        int applied = 0;
        foreach (var state in states)
        {
            if (entryByEntityId.TryGetValue(state.EntityId, out var entry))
            {
                entry.ApplyInitialState(state);
                applied++;
            }
        }

        HomeAssistantPluginLog.Debug($"Initial state applied to {applied}/{states.Count} entries.");
    }
}
