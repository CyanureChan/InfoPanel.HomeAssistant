namespace InfoPanel.HomeAssistant.Core.Configuration;

/// <summary>User-configurable connection and entity filter settings.</summary>
public sealed class HomeAssistantSettings
{
    /// <summary>Default comma-separated domain list when Entity Domains is empty.</summary>
    public const string DefaultEntityDomains = "sensor,climate,binary_sensor";

    /// <summary>Default cap for the domain pool (safeguard; use 0 or * for unlimited).</summary>
    public const int DefaultMaxEntities = 2000;

    /// <summary>Maximum allowed Max Entities value in the Plugins UI.</summary>
    public const int MaxMaxEntities = 10000;

    /// <summary>Default seconds between entity value refresh cycles.</summary>
    public const int DefaultPollIntervalSeconds = 5;

    /// <summary>Minimum allowed poll interval in seconds.</summary>
    public const int MinPollIntervalSeconds = 1;

    /// <summary>Maximum allowed poll interval in seconds.</summary>
    public const int MaxPollIntervalSeconds = 300;

    /// <summary>Poll cycles between forced full dirty apply (safety net).</summary>
    public const int FullSyncPollInterval = 60;

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

    /// <summary>Seconds between entity value refresh cycles.</summary>
    public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;

    /// <summary>Clamps <see cref="PollIntervalSeconds"/> to the allowed range.</summary>
    public int GetEffectivePollIntervalSeconds() =>
        Math.Clamp(PollIntervalSeconds, MinPollIntervalSeconds, MaxPollIntervalSeconds);

    /// <summary>True when URL and token are both set.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>
    /// Expands <see cref="EntityDomains"/> into concrete domain names.
    /// </summary>
    public IReadOnlyList<string> GetDomainFilters() =>
        ExposableEntityDomains.ExpandDomainFilters(
            EntityDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
