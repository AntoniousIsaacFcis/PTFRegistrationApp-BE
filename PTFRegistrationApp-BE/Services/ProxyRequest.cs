namespace PTFRegistrationApp_BE.Services;

public sealed class ProxyRequest
{
    public string Method { get; init; } = string.Empty;

    public string RelativePathWithQuery { get; init; } = string.Empty;

    public string? Body { get; init; }

    public string? Authorization { get; init; }

    public string? ContentType { get; init; }

    public string CorrelationId { get; init; } = string.Empty;
}
