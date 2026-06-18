using InfoPanel.HomeAssistant.Core.Models;
using InfoPanel.HomeAssistant.Core.Rules;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Resolves parsed entity rules to matching Home Assistant entity ids.</summary>
internal static class EntityRuleResolver
{
    /// <summary>
    /// Resolves all rules and returns the union of matched entity ids.
    /// </summary>
    /// <param name="rules">Include or exclude rules to evaluate.</param>
    /// <param name="states">Candidate entity states (already domain-filtered).</param>
    /// <param name="registry">Entity/device registry for device and integration rules.</param>
    /// <returns>Matched entity ids.</returns>
    public static HashSet<string> ResolveRules(
        IEnumerable<EntityRule> rules,
        IReadOnlyList<HomeAssistantEntityState> states,
        HomeAssistantRegistrySnapshot registry)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            foreach (string entityId in ResolveRule(rule, states, registry))
            {
                matched.Add(entityId);
            }
        }

        return matched;
    }

    /// <summary>
    /// Resolves a single rule to matching entity ids.
    /// </summary>
    /// <param name="rule">Rule to evaluate.</param>
    /// <param name="states">Candidate entity states.</param>
    /// <param name="registry">Entity/device registry.</param>
    /// <returns>Matched entity ids; empty when registry is required but unavailable.</returns>
    public static IEnumerable<string> ResolveRule(
        EntityRule rule,
        IReadOnlyList<HomeAssistantEntityState> states,
        HomeAssistantRegistrySnapshot registry)
    {
        switch (rule.Kind)
        {
            case EntityRuleKind.All:
                return states.Select(s => s.EntityId);

            case EntityRuleKind.Entity:
                return states
                    .Where(s => s.EntityId.Equals(rule.Value, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.EntityId);

            case EntityRuleKind.Domain:
                string domain = rule.Value.TrimEnd('.');
                return states
                    .Where(s => s.EntityId.StartsWith($"{domain}.", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.EntityId);

            case EntityRuleKind.Integration:
                if (!registry.IsAvailable)
                {
                    return [];
                }

                return registry.Entities
                    .Where(e => string.Equals(e.Platform, rule.Value, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.EntityId)
                    .Where(id => states.Any(s => s.EntityId.Equals(id, StringComparison.OrdinalIgnoreCase)));

            case EntityRuleKind.Device:
                if (!registry.IsAvailable)
                {
                    return [];
                }

                if (LooksLikeDeviceId(rule.Value))
                {
                    return registry.Entities
                        .Where(e => string.Equals(e.DeviceId, rule.Value, StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.EntityId)
                        .Where(id => states.Any(s => s.EntityId.Equals(id, StringComparison.OrdinalIgnoreCase)));
                }

                var deviceIds = registry.Devices
                    .Where(d => DeviceNameMatches(d, rule.Value))
                    .Select(d => d.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                return registry.Entities
                    .Where(e => e.DeviceId != null && deviceIds.Contains(e.DeviceId))
                    .Select(e => e.EntityId)
                    .Where(id => states.Any(s => s.EntityId.Equals(id, StringComparison.OrdinalIgnoreCase)));

            default:
                return [];
        }
    }

    private static bool LooksLikeDeviceId(string value)
    {
        if (Guid.TryParse(value, out _))
        {
            return true;
        }

        return value.Length == 26 &&
               value.All(static c => (c >= '0' && c <= '9') ||
                                     (c >= 'a' && c <= 'f') ||
                                     (c >= 'A' && c <= 'F'));
    }

    private static bool DeviceNameMatches(DeviceRegistryEntry device, string name) =>
        string.Equals(device.NameByUser, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(device.DisplayName, name, StringComparison.OrdinalIgnoreCase);
}
