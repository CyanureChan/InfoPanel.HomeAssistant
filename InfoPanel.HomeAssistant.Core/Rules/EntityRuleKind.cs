namespace InfoPanel.HomeAssistant.Core.Rules;

/// <summary>Identifies how an entity selection rule matches Home Assistant entities.</summary>
internal enum EntityRuleKind
{
    /// <summary>Wildcard — matches all supported entities.</summary>
    All,

    /// <summary>Exact entity id (e.g. sensor.temperature).</summary>
    Entity,

    /// <summary>Entity domain prefix (e.g. sensor).</summary>
    Domain,

    /// <summary>Integration platform from the entity registry (e.g. bambu_lab).</summary>
    Integration,

    /// <summary>Device by registry id or display name.</summary>
    Device,
}
