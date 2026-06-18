namespace InfoPanel.HomeAssistant.Core.Mapping;

public static class EntityIdHelper
{
    public static string ToEntryId(string entityId) =>
        entityId.Replace('.', '-').ToLowerInvariant();
}
