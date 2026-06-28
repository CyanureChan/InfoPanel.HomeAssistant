namespace InfoPanel.HomeAssistant.Core.Mapping;

public static class EntityIdHelper
{
    public static string ToEntryId(string entityId) =>
        entityId.Replace('.', '-').ToLowerInvariant();

    /// <summary>Converts a plugin entry id back to a Home Assistant entity id.</summary>
    public static string FromEntryId(string entryId)
    {
        int dashIndex = entryId.IndexOf('-', StringComparison.Ordinal);
        return dashIndex <= 0
            ? entryId
            : string.Concat(entryId.AsSpan(0, dashIndex), ".", entryId.AsSpan(dashIndex + 1));
    }
}
