namespace InfoPanel.HomeAssistant.Core.Configuration;

public static class ExposableEntityDomains
{
    /// <summary>
    /// Domains that map cleanly to InfoPanel sensor or text entries.
    /// </summary>
    public static readonly IReadOnlyList<string> Supported =
    [
        "sensor",
        "binary_sensor",
        "climate",
        "number",
        "input_number",
        "switch",
        "input_boolean",
        "cover",
        "lock",
    ];

    private static readonly HashSet<string> SupportedSet =
        new(Supported, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TextDomains =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "binary_sensor",
            "switch",
            "input_boolean",
            "cover",
            "lock",
        };

    public static bool IsWildcard(string? value) =>
        string.Equals(value?.Trim(), "*", StringComparison.Ordinal);

    public static bool IsSupportedEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        int dot = entityId.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        return SupportedSet.Contains(entityId[..dot]);
    }

    public static bool IsTextDomain(string entityId)
    {
        int dot = entityId.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        return TextDomains.Contains(entityId[..dot]);
    }

    public static IReadOnlyList<string> ExpandDomainFilters(IEnumerable<string> filters)
    {
        var list = filters
            .Select(d => d.Trim().TrimEnd('.'))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();

        if (list.Count == 0 || list.Any(IsWildcard))
        {
            return Supported;
        }

        return list;
    }
}
