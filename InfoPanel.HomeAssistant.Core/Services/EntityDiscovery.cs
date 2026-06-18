using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Thin wrapper over <see cref="EntitySelectionEngine"/> for discovery.</summary>
public static class EntityDiscovery
{
    /// <summary>
    /// Filters Home Assistant states using configured include, domain, and exclude rules.
    /// </summary>
    /// <param name="states">All entity states from Home Assistant.</param>
    /// <param name="settings">Filter configuration.</param>
    /// <param name="registry">Entity/device registry for device and integration rules.</param>
    /// <returns>Selection result with counts and matched states.</returns>
    public static EntitySelectionResult Filter(
        IEnumerable<HomeAssistantEntityState> states,
        HomeAssistantSettings settings,
        HomeAssistantRegistrySnapshot registry) =>
        EntitySelectionEngine.Select(states, settings, registry);
}
