using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Observability;

public static class ServiceDefaults
{
    public static IHostApplicationBuilder AddRxServiceDefaults(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables();

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "O";
        });

        builder.Services.AddOtel(builder.Configuration, serviceName);
        return builder;
    }
}
