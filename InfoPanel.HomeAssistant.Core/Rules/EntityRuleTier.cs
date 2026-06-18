namespace InfoPanel.HomeAssistant.Core.Rules;

/// <summary>Determines whether a rule bypasses the domain pool entity cap.</summary>
internal enum EntityRuleTier
{
    /// <summary>Never capped — entity, device, and integration rules.</summary>
    Explicit,

    /// <summary>Subject to pool cap unless wildcard or Max Entities is 0.</summary>
    General,
}
