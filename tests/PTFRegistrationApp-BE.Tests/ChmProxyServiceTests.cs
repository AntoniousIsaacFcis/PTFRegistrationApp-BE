using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PTFRegistrationApp_BE.Config;
using PTFRegistrationApp_BE.Services;
using Xunit;

namespace PTFRegistrationApp_BE.Tests;

public class ChmProxyServiceTests
{
    [Fact]
    public async Task SendAsync_ShouldSanitizeErrors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html>error</html>")
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.chmeetings.com/") };
        var sut = new ChmProxyService(client, Options.Create(new ChmProxyOptions()), NullLogger<ChmProxyService>.Instance);

        var response = await sut.SendAsync(new ProxyRequest
        {
            Method = "GET",
            RelativePathWithQuery = "a",
            CorrelationId = "c1"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Body.Should().Contain("Request failed");
    }

    [Fact]
    public async Task SendAsync_ShouldMapNetworkErrorsToBadGateway()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("boom"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.chmeetings.com/") };
        var sut = new ChmProxyService(client, Options.Create(new ChmProxyOptions()), NullLogger<ChmProxyService>.Instance);

        var response = await sut.SendAsync(new ProxyRequest
        {
            Method = "GET",
            RelativePathWithQuery = "a",
            CorrelationId = "c2"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory(request));
        }
    }
}
