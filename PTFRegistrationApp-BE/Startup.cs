using System;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTFRegistrationApp_BE.Config;
using PTFRegistrationApp_BE.Services;

[assembly: FunctionsStartup(typeof(PTFRegistrationApp_BE.Startup))]

namespace PTFRegistrationApp_BE;

public class Startup : FunctionsStartup
{
    public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
    {
        builder.ConfigurationBuilder
            .AddEnvironmentVariables();
    }

    public override void Configure(IFunctionsHostBuilder builder)
    {
        var configuration = builder.GetContext().Configuration;

        builder.Services.Configure<ChmProxyOptions>(options =>
        {
            options.BaseUrl = configuration["CHM_BASE_URL"] ?? "https://api.chmeetings.com";
            options.ServiceId = configuration["CHM_SERVICE_ID"] ?? "33CE95026156648A";
            options.TimeoutSeconds = ReadInt(configuration["CHM_TIMEOUT_SECONDS"], 30);
            options.RetryCount = ReadInt(configuration["CHM_RETRY_COUNT"], 2);
        });

        builder.Services.Configure<CorsOptions>(options =>
        {
            options.AllowedOriginsRaw = configuration["ALLOWED_ORIGINS"] ?? string.Empty;
        });

        builder.Services
            .AddHttpClient<IChmProxyService, ChmProxyService>((sp, client) =>
            {
                var proxyOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChmProxyOptions>>().Value;
                client.BaseAddress = new Uri(proxyOptions.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            });
    }

    private static int ReadInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
