namespace InfoPanel.HomeAssistant.Core.Rules;

/// <summary>A parsed include or exclude rule for entity selection.</summary>
internal sealed class EntityRule
{
    /// <summary>How this rule matches entities.</summary>
    public EntityRuleKind Kind { get; init; }

    /// <summary>Whether this rule is capped by Max Entities.</summary>
    public EntityRuleTier Tier { get; init; }

    /// <summary>Rule value: entity id, domain, integration, device name/id, or empty for <c>*</c>.</summary>
    public string Value { get; init; } = string.Empty;
}
