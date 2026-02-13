using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Polly;
using Polly.Extensions.Http;
using PTFRegistrationApp_BE.Config;
using PTFRegistrationApp_BE.Helpers;

namespace PTFRegistrationApp_BE.Services;

public sealed class ChmProxyService : IChmProxyService
{
    private readonly HttpClient _httpClient;
    private readonly ChmProxyOptions _options;
    private readonly ILogger<ChmProxyService> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;

    public ChmProxyService(
        HttpClient httpClient,
        IOptions<ChmProxyOptions> options,
        ILogger<ChmProxyService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                Math.Max(0, _options.RetryCount),
                retryAttempt => TimeSpan.FromMilliseconds(250 * retryAttempt));
    }

    public async Task<ProxyResponse> SendAsync(ProxyRequest request, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var watch = Stopwatch.StartNew();

        try
        {
            using var upstreamResponse = await _retryPolicy.ExecuteAsync(async ct =>
            {
                using var upstreamRequest = BuildHttpRequestMessage(request);
                return await _httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }, linkedCts.Token);

            var contentType = upstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
            var body = await upstreamResponse.Content.ReadAsStringAsync(linkedCts.Token);

            _logger.LogInformation(
                "Upstream call finished status={StatusCode} route={Route} durationMs={DurationMs} correlationId={CorrelationId}",
                (int)upstreamResponse.StatusCode,
                request.RelativePathWithQuery,
                watch.ElapsedMilliseconds,
                request.CorrelationId);

            if ((int)upstreamResponse.StatusCode >= 400)
            {
                return new ProxyResponse
                {
                    StatusCode = upstreamResponse.StatusCode,
                    Body = ErrorPayloadHelper.Sanitize(body),
                    CorrelationId = request.CorrelationId,
                    ContentType = "application/json; charset=utf-8"
                };
            }

            return new ProxyResponse
            {
                StatusCode = upstreamResponse.StatusCode,
                Body = body,
                CorrelationId = request.CorrelationId,
                ContentType = contentType
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Upstream timeout route={Route} durationMs={DurationMs} correlationId={CorrelationId}",
                request.RelativePathWithQuery,
                watch.ElapsedMilliseconds,
                request.CorrelationId);

            return new ProxyResponse
            {
                StatusCode = HttpStatusCode.GatewayTimeout,
                Body = JsonConvert.SerializeObject(new { Message = "Upstream timeout.", Type = "Error" }),
                CorrelationId = request.CorrelationId
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Upstream network failure route={Route} durationMs={DurationMs} correlationId={CorrelationId}",
                request.RelativePathWithQuery,
                watch.ElapsedMilliseconds,
                request.CorrelationId);

            return new ProxyResponse
            {
                StatusCode = HttpStatusCode.BadGateway,
                Body = JsonConvert.SerializeObject(new { Message = "Unable to reach upstream service.", Type = "Error" }),
                CorrelationId = request.CorrelationId
            };
        }
    }

    private static HttpRequestMessage BuildHttpRequestMessage(ProxyRequest request)
    {
        var upstreamRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.RelativePathWithQuery);

        if (!string.IsNullOrWhiteSpace(request.Authorization))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Authorization", request.Authorization);
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("x-correlation-id", request.CorrelationId);
        }

        if (!string.IsNullOrEmpty(request.Body))
        {
            upstreamRequest.Content = new StringContent(
                request.Body,
                Encoding.UTF8,
                string.IsNullOrWhiteSpace(request.ContentType) ? "application/json" : request.ContentType);
        }

        return upstreamRequest;
    }
}
