namespace PTFRegistrationApp_BE.Services;

public sealed class ProxyRequest
{
    public required string Method { get; init; }

    public required string RelativePathWithQuery { get; init; }

    public string? Body { get; init; }

    public string? Authorization { get; init; }

    public string? ContentType { get; init; }

    public required string CorrelationId { get; init; }
}
