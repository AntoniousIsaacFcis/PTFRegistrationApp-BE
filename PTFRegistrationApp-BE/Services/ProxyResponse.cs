using System.Net;

namespace PTFRegistrationApp_BE.Services;

public sealed class ProxyResponse
{
    public HttpStatusCode StatusCode { get; init; }

    public string Body { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string ContentType { get; init; } = "application/json; charset=utf-8";
}
