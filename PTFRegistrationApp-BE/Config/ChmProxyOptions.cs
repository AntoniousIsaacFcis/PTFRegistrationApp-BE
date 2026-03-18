namespace PTFRegistrationApp_BE.Config;

public class ChmProxyOptions
{
    public const string SectionName = "ChmProxy";

    public string BaseUrl { get; set; } = "https://api.chmeetings.com";

    public string ServiceId { get; set; } = "33CE95026156648A";

    public int TimeoutSeconds { get; set; } = 30;

    public int RetryCount { get; set; } = 2;
}
