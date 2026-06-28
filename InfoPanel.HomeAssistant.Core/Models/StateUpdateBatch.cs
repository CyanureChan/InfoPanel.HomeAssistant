namespace InfoPanel.HomeAssistant.Core.Models;

/// <summary>Entity states selected for a single plugin update cycle.</summary>
public sealed class StateUpdateBatch
{
    /// <summary>States to apply to plugin entries.</summary>
    public required IReadOnlyList<HomeAssistantEntityState> States { get; init; }

    /// <summary>Active fetch path description for diagnostics.</summary>
    public required string FetchPath { get; init; }

    /// <summary>Dirty entity count before tier filtering.</summary>
    public int DirtyCount { get; init; }

    /// <summary>True when no states needed applying this cycle.</summary>
    public bool Skipped { get; init; }
}
