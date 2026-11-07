using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using FeedFabric.Application.Services.Health;

namespace FeedFabric.Hosting;

// Compiled into the API and the worker (linked from src/Shared), which serve the same two endpoints:
//   /health/live   the process is up and serving; no dependencies are checked. For restarts and Docker HEALTHCHECK.
//   /health/ready  every check tagged "ready" (Postgres, Redis, models, and Kafka and ClamAV in the worker). 503 when one
//                  is unhealthy, 200 when all are healthy or only degraded. For deploy gates and load balancers.
// Both are anonymous and list only each check's name, status and a short reason, never connection details.
public static class HealthEndpoints
{
    public static void MapFeedFabricHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteAsync,
        });
    }

    private static Task WriteAsync(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json";
        http.Response.Headers.CacheControl = "no-store";
        return http.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        }));
    }

    // "dotnet FeedFabric.Api.dll healthcheck": asks this container's own /health/live and exits 0 or 1, for a
    // Docker HEALTHCHECK in images that have no curl
    public static async Task<int> ProbeAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0];
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/health/live");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
