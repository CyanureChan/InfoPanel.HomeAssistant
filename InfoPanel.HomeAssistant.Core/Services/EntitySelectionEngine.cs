using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Models;
using InfoPanel.HomeAssistant.Core.Rules;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>
/// Applies include/exclude rules with tiered caps to produce the final entity set.
/// Exclusions always win over includes at every tier.
/// </summary>
public static class EntitySelectionEngine
{
    /// <summary>
    /// Selects entities from Home Assistant states using configured include, domain, and exclude rules.
    /// </summary>
    /// <param name="states">All entity states from Home Assistant.</param>
    /// <param name="settings">Plugin filter configuration.</param>
    /// <param name="registry">Entity/device registry for device and integration rules.</param>
    /// <returns>Selected entities with explicit and pool counts.</returns>
    public static EntitySelectionResult Select(
        IEnumerable<HomeAssistantEntityState> states,
        HomeAssistantSettings settings,
        HomeAssistantRegistrySnapshot registry)
    {
        var stateList = states
            .Where(s => !string.IsNullOrWhiteSpace(s.EntityId))
            .Where(s => ExposableEntityDomains.IsSupportedEntity(s.EntityId))
            .ToList();

        var includeRules = EntityRuleParser.ParseRules(settings.EntityInclude);
        var excludeRules = EntityRuleParser.ParseRules(settings.EntityExclude);

        var explicitRules = includeRules.Where(r => r.Tier == EntityRuleTier.Explicit).ToList();
        var generalIncludeRules = includeRules.Where(r => r.Tier == EntityRuleTier.General).ToList();
        var domainRules = BuildDomainRules(settings);

        var explicitCandidateIds = EntityRuleResolver.ResolveRules(explicitRules, stateList, registry);
        var poolCandidateIds = EntityRuleResolver.ResolveRules(generalIncludeRules, stateList, registry);
        foreach (string id in EntityRuleResolver.ResolveRules(domainRules, stateList, registry))
        {
            poolCandidateIds.Add(id);
        }

        var excludeIds = EntityRuleResolver.ResolveRules(excludeRules, stateList, registry);

        var explicitIds = explicitCandidateIds
            .Where(id => !excludeIds.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var poolIds = poolCandidateIds
            .Where(id => !excludeIds.Contains(id) && !explicitIds.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int poolMatchedBeforeCap = poolIds.Count;
        var poolSelectedIds = IsPoolUncapped(settings, generalIncludeRules, domainRules)
            ? poolIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : poolIds.Take(ResolvePoolCap(settings)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedIds = explicitIds
            .Concat(poolSelectedIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = stateList
            .Where(s => selectedIds.Contains(s.EntityId))
            .GroupBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EntitySelectionResult
        {
            Selected = selected,
            ExplicitCount = explicitIds.Count,
            PoolCount = poolSelectedIds.Count,
            PoolMatchedBeforeCap = poolMatchedBeforeCap,
        };
    }

    private static IReadOnlyList<EntityRule> BuildDomainRules(HomeAssistantSettings settings)
    {
        return settings.GetDomainFilters()
            .Select(domain => new EntityRule
            {
                Kind = EntityRuleKind.Domain,
                Tier = EntityRuleTier.General,
                Value = domain,
            })
            .ToList();
    }

    private static bool IsPoolUncapped(
        HomeAssistantSettings settings,
        IReadOnlyList<EntityRule> generalIncludeRules,
        IReadOnlyList<EntityRule> domainRules)
    {
        if (settings.MaxEntities <= 0)
        {
            return true;
        }

        if (generalIncludeRules.Any(static r => r.Kind == EntityRuleKind.All))
        {
            return true;
        }

        if (domainRules.Any(static r => r.Kind == EntityRuleKind.All))
        {
            return true;
        }

        return EntityRuleParser.Tokenize(settings.EntityInclude).Any(ExposableEntityDomains.IsWildcard) ||
               EntityRuleParser.Tokenize(settings.EntityDomains).Any(ExposableEntityDomains.IsWildcard);
    }

    private static int ResolvePoolCap(HomeAssistantSettings settings)
    {
        if (settings.MaxEntities <= 0)
        {
            return int.MaxValue;
        }

        return settings.MaxEntities;
    }
}
