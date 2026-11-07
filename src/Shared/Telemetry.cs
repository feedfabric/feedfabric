using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using FeedFabric.Application.Services;

namespace FeedFabric.Hosting;

// Compiled into the API and the worker (linked from src/Shared). Traces and metrics for HTTP, Postgres (which
// includes every EF Core query), Redis and the process, plus FeedFabric's own sources and meters. They're sent over
// OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (a collector, Grafana, Honeycomb, Datadog...), with logs too;
// OTEL_SERVICE_NAME and the other standard OTEL_* variables apply. Without an endpoint nothing is exported.
public static class Telemetry
{
    public static WebApplicationBuilder AddFeedFabricTelemetry(this WebApplicationBuilder builder, string serviceName,
        string[] sources, string[] meters)
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(tracing => tracing
                // Probes every few seconds would drown the traces that matter
                .AddAspNetCoreInstrumentation(options => options.Filter = http => !http.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddRedisInstrumentation()
                .AddSource(sources))
            .WithMetrics(metrics => metrics
                // http.server.request.duration by route gives the feed routes' p95
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql")
                .AddMeter(AppMetrics.MeterName)
                .AddMeter(meters));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeScopes = true;
                logging.IncludeFormattedMessage = true;
            });
            otel.UseOtlpExporter();
        }
        return builder;
    }
}
