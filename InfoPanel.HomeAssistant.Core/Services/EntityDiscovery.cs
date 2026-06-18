using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

public static class EntityDiscovery
{
    public static (IReadOnlyList<HomeAssistantEntityState> Selected, int MatchedBeforeCap) Filter(
        IEnumerable<HomeAssistantEntityState> states,
        HomeAssistantSettings settings,
        HomeAssistantRegistrySnapshot registry) =>
        EntityIncludeParser.Select(states, settings, registry);

    public static IReadOnlyList<HomeAssistantEntityState> FilterByDomains(
        IEnumerable<HomeAssistantEntityState> states,
        HomeAssistantSettings settings)
    {
        var domains = settings.GetDomainFilters();
        if (domains.Count == 0)
        {
            return [];
        }

        return states
            .Where(s => !string.IsNullOrWhiteSpace(s.EntityId))
            .Where(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId))
            .Where(s => domains.Any(d => s.EntityId.StartsWith($"{d}.", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
