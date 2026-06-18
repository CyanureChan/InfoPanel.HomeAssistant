using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

internal enum EntityIncludeRuleKind
{
    All,
    Entity,
    Domain,
    Integration,
    Device,
}

internal sealed class EntityIncludeRule
{
    public EntityIncludeRuleKind Kind { get; init; }
    public string Value { get; init; } = string.Empty;
}

public static class EntityIncludeParser
{
    public static IReadOnlyList<string> ParseRuleLines(string? entityInclude)
    {
        if (string.IsNullOrWhiteSpace(entityInclude))
        {
            return [];
        }

        return entityInclude
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToList();
    }

    public static bool UsesExplicitRules(string? entityInclude) =>
        ParseRuleLines(entityInclude).Count > 0;

    public static (IReadOnlyList<HomeAssistantEntityState> Selected, int MatchedBeforeCap) Select(
        IEnumerable<HomeAssistantEntityState> states,
        HomeAssistantSettings settings,
        HomeAssistantRegistrySnapshot registry)
    {
        var stateList = states
            .Where(s => !string.IsNullOrWhiteSpace(s.EntityId))
            .Where(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId))
            .ToList();

        var rules = ParseRuleLines(settings.EntityInclude)
            .Select(ParseRule)
            .ToList();

        IReadOnlyList<HomeAssistantEntityState> ordered;
        if (rules.Count == 0)
        {
            ordered = EntityDiscovery.FilterByDomains(stateList, settings);
        }
        else
        {
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                foreach (string entityId in ResolveRule(rule, stateList, registry))
                {
                    if (ExposableEntityDomains.IsSupportedEntity(entityId))
                    {
                        matched.Add(entityId);
                    }
                }
            }

            ordered = stateList
                .Where(s => matched.Contains(s.EntityId))
                .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        int max = settings.MaxEntities > 0 ? settings.MaxEntities : HomeAssistantSettings.DefaultMaxEntities;
        int matchedBeforeCap = ordered.Count;
        var selected = ordered.Take(max).ToList();
        return (selected, matchedBeforeCap);
    }

    private static EntityIncludeRule ParseRule(string line)
    {
        if (line == "*")
        {
            return new EntityIncludeRule { Kind = EntityIncludeRuleKind.All };
        }

        int dot = line.IndexOf('.');
        if (dot <= 0 || dot == line.Length - 1)
        {
            return new EntityIncludeRule { Kind = EntityIncludeRuleKind.Entity, Value = line };
        }

        string prefix = line[..dot];
        string value = line[(dot + 1)..];

        return prefix.ToLowerInvariant() switch
        {
            "entity" => new EntityIncludeRule { Kind = EntityIncludeRuleKind.Entity, Value = value },
            "domain" => new EntityIncludeRule { Kind = EntityIncludeRuleKind.Domain, Value = value },
            "integration" => new EntityIncludeRule { Kind = EntityIncludeRuleKind.Integration, Value = value },
            "device" => new EntityIncludeRule { Kind = EntityIncludeRuleKind.Device, Value = value },
            _ => new EntityIncludeRule { Kind = EntityIncludeRuleKind.Entity, Value = line },
        };
    }

    private static IEnumerable<string> ResolveRule(
        EntityIncludeRule rule,
        IReadOnlyList<HomeAssistantEntityState> states,
        HomeAssistantRegistrySnapshot registry)
    {
        switch (rule.Kind)
        {
            case EntityIncludeRuleKind.All:
                return states.Select(s => s.EntityId);

            case EntityIncludeRuleKind.Entity:
                return states
                    .Where(s => s.EntityId.Equals(rule.Value, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.EntityId);

            case EntityIncludeRuleKind.Domain:
                string domain = rule.Value.TrimEnd('.');
                return states
                    .Where(s => s.EntityId.StartsWith($"{domain}.", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.EntityId);

            case EntityIncludeRuleKind.Integration:
                if (!registry.IsAvailable)
                {
                    return [];
                }

                return registry.Entities
                    .Where(e => string.Equals(e.Platform, rule.Value, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.EntityId)
                    .Where(id => states.Any(s => s.EntityId.Equals(id, StringComparison.OrdinalIgnoreCase)));

            case EntityIncludeRuleKind.Device:
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
