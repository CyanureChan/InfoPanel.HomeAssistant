namespace InfoPanel.HomeAssistant.Core.Configuration;

/// <summary>User-configurable connection and entity filter settings.</summary>
public sealed class HomeAssistantSettings
{
    /// <summary>Default comma-separated domain list when Entity Domains is empty.</summary>
    public const string DefaultEntityDomains = "sensor,climate,binary_sensor";

    /// <summary>Default cap for the domain pool when Max Entities is not set.</summary>
    public const int DefaultMaxEntities = 50;

    /// <summary>Home Assistant base URL without trailing slash.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Long-lived access token.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Comma-separated domain filters or <c>*</c>.</summary>
    public string EntityDomains { get; set; } = DefaultEntityDomains;

    /// <summary>Comma-separated include rules.</summary>
    public string EntityInclude { get; set; } = string.Empty;

    /// <summary>Comma-separated exclude rules; always override includes.</summary>
    public string EntityExclude { get; set; } = string.Empty;

    /// <summary>Domain pool cap; 0 means unlimited.</summary>
    public int MaxEntities { get; set; } = DefaultMaxEntities;

    /// <summary>True when URL and token are both set.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>
    /// Expands <see cref="EntityDomains"/> into concrete domain names.
    /// </summary>
    /// <returns>Domain list; <c>*</c> expands to all supported domains.</returns>
    public IReadOnlyList<string> GetDomainFilters() =>
        ExposableEntityDomains.ExpandDomainFilters(
            EntityDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
