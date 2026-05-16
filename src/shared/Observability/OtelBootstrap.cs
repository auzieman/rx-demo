
// /src/shared/Observability/OtelBootstrap.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Instrumentation.AspNetCore; // for AddAspNetCoreInstrumentation
using MassTransit.Logging;
using Shared.Telemetry;


namespace Shared.Observability
{
    public static class OtelBootstrap
    {
        public static IServiceCollection AddOtel(this IServiceCollection services, IConfiguration cfg, string serviceName)
        {
            var env   = cfg["Otel:Environment"]       ?? "dev";
            var ns    = cfg["Otel:ServiceNamespace"]  ?? "rx";
            var baseOtlp = (cfg["Otel:Endpoint"]      ?? "http://otel-collector:4318").TrimEnd('/');

            // Allow standard OTEL env var override if present (useful for cross-language consistency)
            var envOtlp = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(envOtlp))
                baseOtlp = envOtlp.TrimEnd('/');

            // Sampler: honor appsettings TraceSampleRatio if provided
            var ratioStr = cfg["Otel:TraceSampleRatio"];
            var ratio = 1.0;
            if (double.TryParse(ratioStr, out var parsed) && parsed >= 0 && parsed <= 1)
                ratio = parsed;

            Metrics.ConfigureComponent(cfg, serviceName);

            services.AddOpenTelemetry()
                .ConfigureResource(rb => rb
                    .AddService(serviceName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = env,
                        ["service.namespace"]      = ns
                    }))
                .WithTracing(t =>
                {
                    // MassTransit ActivitySource (hardcode name to avoid version-dependent alias)
                    t.AddSource("MassTransit")
                     .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio)))
                     .AddAspNetCoreInstrumentation()        // server spans (remove if not needed)
                     .AddHttpClientInstrumentation()
                     .AddSqlClientInstrumentation(opt =>
                     {
                         opt.RecordException = true;
                     });

                    // Explicit HTTP endpoints per signal
                    t.AddOtlpExporter(o =>
                    {
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                        o.Endpoint = new Uri($"{baseOtlp}/v1/traces");
                    });

                    // Optional: console exporter for quick validation (guard with env flag)
                    if (Environment.GetEnvironmentVariable("OTEL_CONSOLE") == "1")
                        t.AddConsoleExporter();
                })
                .WithMetrics(m =>
                {
                    m.AddMeter("rx.meter")
                     .AddMeter("MassTransit")
                     .AddHttpClientInstrumentation()
                     .AddRuntimeInstrumentation()
                     .AddView("rx.api.requests_total", new MetricStreamConfiguration { TagKeys = new[] { "route", "method", "result" }})
                     .AddView("rx.api.duration_ms", new ExplicitBucketHistogramConfiguration
                     {
                         Boundaries = new double[] { 5,10,25,50,100,250,500,1000,2500,5000 },
                         TagKeys = new[] { "route", "method" }
                     })
                     .AddView("rx.queue.messages_total", new MetricStreamConfiguration { TagKeys = new[] { "queue", "direction", "message_type", "result" }})
                     .AddView("rx.process.duration_ms", new ExplicitBucketHistogramConfiguration
                     {
                         Boundaries = new double[] { 5,10,25,50,100,250,500,1000,2500,5000 },
                         TagKeys = new[] { "queue", "message_type" }
                     })
                     .AddView("rx.message.publish_total", new MetricStreamConfiguration { TagKeys = new[] { "queue", "message_type", "result" }})
                     .AddView("rx.db.op_total", new MetricStreamConfiguration { TagKeys = new[] { "operation", "result" }})
                     .AddView("rx.db.duration_ms", new ExplicitBucketHistogramConfiguration
                     {
                         Boundaries = new double[] { 5,10,25,50,100,250,500,1000,2500,5000 },
                         TagKeys = new[] { "operation", "result" }
                     })
                     .AddView("rx.db.payload.bytes", new MetricStreamConfiguration { TagKeys = new[] { "operation" } })
                     .AddView("rx.cache.ops_total", new MetricStreamConfiguration { TagKeys = new[] { "cache", "operation", "result" } })
                     .AddView("rx.cache.duration_ms", new ExplicitBucketHistogramConfiguration
                     {
                         Boundaries = new double[] { 1,5,10,25,50,100,250,500,1000 },
                         TagKeys = new[] { "cache", "operation", "result" }
                     })
                     .AddView("rx.errors_total", new MetricStreamConfiguration { TagKeys = new[] { "source", "error_type" } });

                    m.AddOtlpExporter((exp, reader) =>
                    {
                        exp.Protocol = OtlpExportProtocol.HttpProtobuf;
                        exp.Endpoint = new Uri($"{baseOtlp}/v1/metrics");
                        reader.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                    });

                    if (Environment.GetEnvironmentVariable("OTEL_CONSOLE") == "1")
                        m.AddConsoleExporter();
                })
                .WithLogging(logBuilder =>
                {
                    logBuilder.AddOtlpExporter(o =>
                    {
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                        o.Endpoint = new Uri($"{baseOtlp}/v1/logs");
                    });

                    if (Environment.GetEnvironmentVariable("OTEL_CONSOLE") == "1")
                        logBuilder.AddConsoleExporter(); // ✅ OTel logs console exporter

                }, options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });

            return services;
        }
    }
}
