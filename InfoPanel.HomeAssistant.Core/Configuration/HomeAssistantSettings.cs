namespace InfoPanel.HomeAssistant.Core.Configuration;

public sealed class HomeAssistantSettings
{
    public const string DefaultEntityDomains = "sensor,climate,binary_sensor";
    public const int DefaultMaxEntities = 20;

    public string BaseUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string EntityDomains { get; set; } = DefaultEntityDomains;
    public string EntityInclude { get; set; } = string.Empty;
    public int MaxEntities { get; set; } = DefaultMaxEntities;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(AccessToken);

    public IReadOnlyList<string> GetDomainFilters() =>
        ExposableEntityDomains.ExpandDomainFilters(
            EntityDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
