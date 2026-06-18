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
    /// <param name="runtime">Shared Home Assistant runtime.</param>
    /// <param name="settings">Configuration to apply.</param>
    public static void ApplySettings(HomeAssistantRuntime runtime, Core.Configuration.HomeAssistantSettings settings) =>
        runtime.ApplySettings(settings);

    /// <summary>
    /// Discovers entities and maps them to plugin layout groups.
    /// </summary>
    /// <param name="runtime">Configured runtime with API access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Layout with grouped entity entries and discovery metadata.</returns>
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
            ExplicitCount = discovery.ExplicitCount,
            PoolCount = discovery.PoolCount,
            PoolMatchedBeforeCap = discovery.PoolMatchedBeforeCap,
            SupportedEntityCount = discovery.SupportedEntityCount,
        };
    }

    /// <summary>
    /// Registers discovered groups and the system container with the plugin host.
    /// </summary>
    /// <param name="containers">Host container list from <see cref="BasePlugin.Load"/>.</param>
    /// <param name="layout">Discovered entity layout.</param>
    /// <param name="connectionStatus">Connection status text entry.</param>
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
    /// <param name="container">Target container.</param>
    /// <param name="entries">Entity entries to register.</param>
    public static void RegisterEntries(IPluginContainer container, IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            container.Entries.Add(entry.Data);
        }
    }

    /// <summary>Updates all entity entries from fresh Home Assistant states.</summary>
    /// <param name="entries">Entries to update.</param>
    /// <param name="states">Current entity states keyed by entity id.</param>
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
