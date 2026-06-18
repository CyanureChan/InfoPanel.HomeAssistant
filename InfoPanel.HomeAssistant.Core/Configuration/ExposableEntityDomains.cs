namespace InfoPanel.HomeAssistant.Core.Configuration;

/// <summary>Home Assistant entity domains exposed as InfoPanel sensors or text entries.</summary>
public static class ExposableEntityDomains
{
    /// <summary>Domains included when using <c>*</c> or domain rules.</summary>
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
        "light",
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
            "light",
        };

    /// <summary>True when the token is a wildcard (<c>*</c>).</summary>
    public static bool IsWildcard(string? value) =>
        string.Equals(value?.Trim(), "*", StringComparison.Ordinal);

    /// <summary>
    /// True when the entity id belongs to a supported domain.
    /// </summary>
    /// <param name="entityId">Full entity id (e.g. sensor.temperature).</param>
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

    /// <summary>
    /// True when the entity should map to a text entry rather than a numeric sensor.
    /// </summary>
    /// <param name="entityId">Full entity id.</param>
    public static bool IsTextDomain(string entityId)
    {
        int dot = entityId.IndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        return TextDomains.Contains(entityId[..dot]);
    }

    /// <summary>
    /// Expands domain filter tokens into concrete domain names.
    /// </summary>
    /// <param name="filters">Raw domain tokens from configuration.</param>
    /// <returns>Supported domains; empty or <c>*</c> returns all supported domains.</returns>
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
