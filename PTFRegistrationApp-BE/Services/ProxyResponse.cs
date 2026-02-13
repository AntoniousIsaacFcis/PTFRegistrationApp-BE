using System.Net;

namespace PTFRegistrationApp_BE.Services;

public sealed class ProxyResponse
{
    public required HttpStatusCode StatusCode { get; init; }

    public required string Body { get; init; }

    public required string CorrelationId { get; init; }

    public string ContentType { get; init; } = "application/json; charset=utf-8";
}
