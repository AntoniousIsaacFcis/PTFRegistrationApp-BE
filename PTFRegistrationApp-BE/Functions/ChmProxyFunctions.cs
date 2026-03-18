using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PTFRegistrationApp_BE.Config;
using PTFRegistrationApp_BE.Services;

namespace PTFRegistrationApp_BE.Functions;

public class ChmProxyFunctions
{
    private readonly IChmProxyService _proxyService;
    private readonly ChmProxyOptions _proxyOptions;
    private readonly CorsOptions _corsOptions;

    public ChmProxyFunctions(
        IChmProxyService proxyService,
        IOptions<ChmProxyOptions> proxyOptions,
        IOptions<CorsOptions> corsOptions)
    {
        _proxyService = proxyService;
        _proxyOptions = proxyOptions.Value;
        _corsOptions = corsOptions.Value;
    }

    [FunctionName("Health")]
    [OpenApiOperation(operationId: "Health", tags: new[] { "System" })]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object))]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] HttpRequest req,
        ILogger log)
    {
        var correlationId = EnsureCorrelationId(req);
        log.LogInformation("Health check correlationId={CorrelationId}", correlationId);

        var result = new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json; charset=utf-8",
            Content = JsonConvert.SerializeObject(new { status = "ok", correlationId })
        };

        AddResponseHeaders(req, result, correlationId, "GET");
        return result;
    }

    [FunctionName("Signin")]
    public Task<IActionResult> Signin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/Signin")] HttpRequest req,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "POST", "Account/Signin", ValidateSigninBodyAsync, authorizationRequired: false);

    [FunctionName("ListEvents")]
    public Task<IActionResult> ListEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "{serviceId}/Meetings/Event/ListEvents")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "GET", $"{serviceId}/Meetings/Event/ListEvents{req.QueryString}", ValidateListEventsQuery, true, serviceId);

    [FunctionName("EventDetails")]
    public Task<IActionResult> EventDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "{serviceId}/Meetings/Event/EventDetails")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "GET", $"{serviceId}/Meetings/Event/EventDetails{req.QueryString}", ValidateEventDetailsQuery, true, serviceId);

    [FunctionName("ListEventSchedules")]
    public Task<IActionResult> ListEventSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "{serviceId}/Meetings/Event/ListEventSchedules")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "GET", $"{serviceId}/Meetings/Event/ListEventSchedules{req.QueryString}", ValidateListEventSchedulesQuery, true, serviceId);

    [FunctionName("AddOrRemoveAttendance")]
    public Task<IActionResult> AddOrRemoveAttendance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "{serviceId}/Meetings/Schedule/AddOrRemoveAttendance")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "POST", $"{serviceId}/Meetings/Schedule/AddOrRemoveAttendance", ValidateAttendanceBodyAsync, true, serviceId);

    [FunctionName("ListMembers")]
    public Task<IActionResult> ListMembers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "{serviceId}/Core/Member/ListMembers")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "POST", $"{serviceId}/Core/Member/ListMembers", ValidateJsonBodyAsync, true, serviceId);

    [FunctionName("ListMemberSchedules")]
    public Task<IActionResult> ListMemberSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "{serviceId}/Meetings/Schedule/ListMemberSchedules")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "GET", $"{serviceId}/Meetings/Schedule/ListMemberSchedules{req.QueryString}", ValidateMemberRangeQuery, true, serviceId);

    [FunctionName("GetMemberMeetingsInfo")]
    public Task<IActionResult> GetMemberMeetingsInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "{serviceId}/Core/Member/GetMemberMeetingsInfo")] HttpRequest req,
        string serviceId,
        ILogger log,
        CancellationToken ct)
        => HandleAsync(req, log, ct, "GET", $"{serviceId}/Core/Member/GetMemberMeetingsInfo{req.QueryString}", ValidateMemberRangeQuery, true, serviceId);

    private async Task<IActionResult> HandleAsync(
        HttpRequest req,
        ILogger log,
        CancellationToken ct,
        string expectedMethod,
        string route,
        Func<HttpRequest, Task<string?>> validator,
        bool authorizationRequired,
        string? serviceId = null)
    {
        var correlationId = EnsureCorrelationId(req);

        if (HttpMethods.IsOptions(req.Method))
        {
            var preflight = new StatusCodeResult(StatusCodes.Status204NoContent);
            AddResponseHeaders(req, preflight, correlationId, expectedMethod);
            return preflight;
        }

        if (!string.Equals(req.Method, expectedMethod, StringComparison.OrdinalIgnoreCase))
        {
            return BuildError(req, correlationId, StatusCodes.Status405MethodNotAllowed, "Method not allowed.", expectedMethod);
        }

        if (!string.IsNullOrWhiteSpace(serviceId) && !string.Equals(serviceId, _proxyOptions.ServiceId, StringComparison.Ordinal))
        {
            return BuildError(req, correlationId, StatusCodes.Status400BadRequest, "Invalid service id.", expectedMethod);
        }

        var validationError = await validator(req);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BuildError(req, correlationId, StatusCodes.Status400BadRequest, validationError, expectedMethod);
        }

        var authorization = req.Headers["Authorization"].FirstOrDefault();
        if (authorizationRequired && string.IsNullOrWhiteSpace(authorization))
        {
            return BuildError(req, correlationId, StatusCodes.Status400BadRequest, "Authorization header is required.", expectedMethod);
        }

        var body = await ReadBodyAsync(req);

        var proxyResponse = await _proxyService.SendAsync(new ProxyRequest
        {
            Method = req.Method,
            RelativePathWithQuery = route,
            Authorization = authorization,
            Body = body,
            ContentType = req.ContentType,
            CorrelationId = correlationId
        }, ct);

        log.LogInformation(
            "Proxy request completed route={Route} status={StatusCode} correlationId={CorrelationId}",
            route,
            (int)proxyResponse.StatusCode,
            correlationId);

        var result = new ContentResult
        {
            StatusCode = (int)proxyResponse.StatusCode,
            ContentType = proxyResponse.ContentType,
            Content = proxyResponse.Body
        };

        AddResponseHeaders(req, result, correlationId, expectedMethod);
        return result;
    }

    private static Task<string?> ValidateListEventsQuery(HttpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Query["StartDate"]) || string.IsNullOrWhiteSpace(req.Query["EndDate"]))
        {
            return Task.FromResult<string?>("StartDate and EndDate are required.");
        }

        return Task.FromResult<string?>(null);
    }

    private static Task<string?> ValidateEventDetailsQuery(HttpRequest req)
    {
        return Task.FromResult(string.IsNullOrWhiteSpace(req.Query["eventId"]) ? "eventId is required." : null);
    }

    private static Task<string?> ValidateListEventSchedulesQuery(HttpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Query["eventId"]) ||
            string.IsNullOrWhiteSpace(req.Query["StartDate"]) ||
            string.IsNullOrWhiteSpace(req.Query["EndDate"]))
        {
            return Task.FromResult<string?>("eventId, StartDate, and EndDate are required.");
        }

        return Task.FromResult<string?>(null);
    }

    private static Task<string?> ValidateMemberRangeQuery(HttpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Query["MemberId"]) || string.IsNullOrWhiteSpace(req.Query["From"]))
        {
            return Task.FromResult<string?>("MemberId and From are required.");
        }

        return Task.FromResult<string?>(null);
    }

    private async Task<string?> ValidateSigninBodyAsync(HttpRequest req)
    {
        var body = await ReadBodyAsync(req);
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Body is required.";
        }

        try
        {
            var json = JObject.Parse(body);
            if (string.IsNullOrWhiteSpace((string?)json["UserName"]) || string.IsNullOrWhiteSpace((string?)json["Password"]))
            {
                return "UserName and Password are required.";
            }

            var serviceId = (string?)json["ServiceId"];
            if (!string.Equals(serviceId, _proxyOptions.ServiceId, StringComparison.Ordinal))
            {
                return "Invalid ServiceId.";
            }
        }
        catch
        {
            return "Invalid JSON payload.";
        }

        return null;
    }

    private static async Task<string?> ValidateAttendanceBodyAsync(HttpRequest req)
    {
        var body = await ReadBodyAsync(req);
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Body is required.";
        }

        try
        {
            var jsonArray = JArray.Parse(body);
            if (!jsonArray.Any())
            {
                return "At least one attendance item is required.";
            }

            if (jsonArray.Any(item => item["MemberId"] == null || item["ScheduleId"] == null || item["IsCheckIn"] == null))
            {
                return "Each attendance item must include MemberId, IsCheckIn, and ScheduleId.";
            }
        }
        catch
        {
            return "Invalid JSON payload.";
        }

        return null;
    }

    private static async Task<string?> ValidateJsonBodyAsync(HttpRequest req)
    {
        var body = await ReadBodyAsync(req);
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Body is required.";
        }

        try
        {
            JToken.Parse(body);
            return null;
        }
        catch
        {
            return "Invalid JSON payload.";
        }
    }

    private static async Task<string> ReadBodyAsync(HttpRequest req)
    {
        const string cacheKey = "__rawBody";
        if (req.HttpContext.Items.TryGetValue(cacheKey, out var cached) && cached is string cachedBody)
        {
            return cachedBody;
        }

        string body;
        if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
            using var reader = new StreamReader(req.Body, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            req.Body.Position = 0;
        }
        else
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            body = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        req.HttpContext.Items[cacheKey] = body;
        return body;
    }

    private string EnsureCorrelationId(HttpRequest req)
    {
        var correlationId = req.Headers["x-correlation-id"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
    }

    private IActionResult BuildError(HttpRequest req, string correlationId, int statusCode, string message, string method)
    {
        var result = new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json; charset=utf-8",
            Content = JsonConvert.SerializeObject(new { Message = message, Type = "Error" })
        };

        AddResponseHeaders(req, result, correlationId, method);
        return result;
    }

    private void AddResponseHeaders(HttpRequest req, IActionResult result, string correlationId, string method)
    {
        if (result is ContentResult contentResult)
        {
            req.HttpContext.Response.Headers["x-correlation-id"] = correlationId;
            ApplyCorsHeaders(req, method);
            contentResult.ContentType ??= "application/json; charset=utf-8";
        }
        else if (result is StatusCodeResult)
        {
            req.HttpContext.Response.Headers["x-correlation-id"] = correlationId;
            ApplyCorsHeaders(req, method);
        }
    }

    private void ApplyCorsHeaders(HttpRequest req, string method)
    {
        var origin = req.Headers["Origin"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(origin) && _corsOptions.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            req.HttpContext.Response.Headers["Access-Control-Allow-Origin"] = origin;
            req.HttpContext.Response.Headers["Vary"] = "Origin";
            req.HttpContext.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type, x-correlation-id";
            req.HttpContext.Response.Headers["Access-Control-Allow-Methods"] = $"{method}, OPTIONS";
        }
    }
}
