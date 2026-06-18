namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Result of applying include/exclude rules to Home Assistant entity states.</summary>
public sealed class EntitySelectionResult
{
    /// <summary>Final selected entity states.</summary>
    public required IReadOnlyList<HomeAssistantEntityState> Selected { get; init; }

    /// <summary>Entities matched by explicit-tier rules after exclusions.</summary>
    public int ExplicitCount { get; init; }

    /// <summary>Entities matched by the domain pool after cap and exclusions.</summary>
    public int PoolCount { get; init; }

    /// <summary>Domain pool matches before the Max Entities cap.</summary>
    public int PoolMatchedBeforeCap { get; init; }

    /// <summary>Total selected entity count.</summary>
    public int TotalCount => Selected.Count;

    /// <summary>Total matches before the domain pool cap is applied.</summary>
    public int MatchedBeforeCap => ExplicitCount + PoolMatchedBeforeCap;
}
