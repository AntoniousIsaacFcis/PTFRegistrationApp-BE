using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using PTFRegistrationApp_BE.Config;
using PTFRegistrationApp_BE.Functions;
using PTFRegistrationApp_BE.Services;
using Xunit;

namespace PTFRegistrationApp_BE.Tests;

public class ChmProxyFunctionsTests
{
    [Fact]
    public async Task Signin_ShouldReturn400_WhenServiceIdIsInvalid()
    {
        var sut = CreateSut();
        var req = BuildRequest("POST", "/Account/Signin", "{\"UserName\":\"a\",\"Password\":\"b\",\"ServiceId\":\"bad\"}");

        var result = await sut.Signin(req, NullLogger.Instance, CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task EventDetails_ShouldRejectMissingQuery()
    {
        var sut = CreateSut();
        var req = BuildRequest("GET", "/33CE95026156648A/Meetings/Event/EventDetails", null);

        var result = await sut.EventDetails(req, "33CE95026156648A", NullLogger.Instance, CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ListEventSchedules_ShouldRejectMissingQuery()
    {
        var sut = CreateSut();
        var req = BuildRequest("GET", "/33CE95026156648A/Meetings/Event/ListEventSchedules?eventId=1120861", null);

        var result = await sut.ListEventSchedules(req, "33CE95026156648A", NullLogger.Instance, CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ListEvents_ShouldForward_WhenValid()
    {
        var proxy = new FakeProxyService();
        var sut = CreateSut(proxy);
        var req = BuildRequest("GET", "/33CE95026156648A/Meetings/Event/ListEvents?StartDate=2024-01-01&EndDate=2024-01-02", null);
        req.Headers["Authorization"] = "Bearer token";

        var result = await sut.ListEvents(req, "33CE95026156648A", NullLogger.Instance, CancellationToken.None);

        proxy.LastRequest.Should().NotBeNull();
        proxy.LastRequest!.RelativePathWithQuery.Should().Contain("StartDate=2024-01-01");
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be((int)HttpStatusCode.OK);
        JObject.Parse(content.Content!).Value<string>("Message").Should().Be("ok");
    }

    [Fact]
    public async Task ListEventSchedules_ShouldForward_WhenValid()
    {
        var proxy = new FakeProxyService();
        var sut = CreateSut(proxy);
        var req = BuildRequest("GET", "/33CE95026156648A/Meetings/Event/ListEventSchedules?eventId=1120861&StartDate=2026-03-01&EndDate=2026-05-31&SearchText=", null);
        req.Headers["Authorization"] = "Bearer token";

        var result = await sut.ListEventSchedules(req, "33CE95026156648A", NullLogger.Instance, CancellationToken.None);

        proxy.LastRequest.Should().NotBeNull();
        proxy.LastRequest!.RelativePathWithQuery.Should().Contain("ListEventSchedules");
        proxy.LastRequest.RelativePathWithQuery.Should().Contain("eventId=1120861");
        proxy.LastRequest.RelativePathWithQuery.Should().Contain("StartDate=2026-03-01");
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be((int)HttpStatusCode.OK);
        JObject.Parse(content.Content!).Value<string>("Message").Should().Be("ok");
    }

    [Fact]
    public async Task AddOrRemoveAttendance_ShouldRejectNonPositiveScheduleId()
    {
        var sut = CreateSut();
        var req = BuildRequest("POST", "/33CE95026156648A/Meetings/Schedule/AddOrRemoveAttendance", "[{\"MemberId\":1,\"IsCheckIn\":true,\"ScheduleId\":0}]");
        req.Headers["Authorization"] = "Bearer token";

        var result = await sut.AddOrRemoveAttendance(req, "33CE95026156648A", NullLogger.Instance, CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(400);
        content.Content.Should().Contain("positive ScheduleId");
        content.Content.Should().Contain("ListEventSchedules");
    }

    private static ChmProxyFunctions CreateSut(FakeProxyService? proxy = null)
    {
        return new ChmProxyFunctions(
            proxy ?? new FakeProxyService(),
            Options.Create(new ChmProxyOptions { ServiceId = "33CE95026156648A" }),
            Options.Create(new CorsOptions { AllowedOriginsRaw = "http://localhost:4200" }));
    }

    private static HttpRequest BuildRequest(string method, string pathAndQuery, string? body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;

        var split = pathAndQuery.Split('?', 2);
        context.Request.Path = split[0];
        if (split.Length > 1)
        {
            context.Request.QueryString = new QueryString("?" + split[1]);
        }

        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";

        return context.Request;
    }

    private sealed class FakeProxyService : IChmProxyService
    {
        public ProxyRequest? LastRequest { get; private set; }

        public Task<ProxyResponse> SendAsync(ProxyRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ProxyResponse
            {
                StatusCode = HttpStatusCode.OK,
                Body = "{\"Message\":\"ok\"}",
                CorrelationId = request.CorrelationId,
                ContentType = "application/json"
            });
        }
    }
}
