using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Application.Services.Storage;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Health;

// Readiness checks shared by the API and the worker (/health/ready). Each is tagged "ready"; liveness (/health/live)
// runs none of them, so a dependency being down never gets a healthy process restarted.
public static class HealthTags
{
    public const string Ready = "ready";
}

public sealed class PostgresHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Can't connect to Postgres.");
}

// The API runs without Redis (feeds fall back to Postgres), so it registers this as Degraded on failure; the worker,
// which can't fan out without it, as Unhealthy
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis isn't answering.");
        }
    }
}

// Only registered while MalwareScanning:Enabled: without clamd, uploads wait in quarantine
public sealed class ClamAvHealthCheck(IMalwareScanner scanner, IOptions<MalwareScanningOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return HealthCheckResult.Healthy("Malware scanning is off.");
        }
        return await scanner.PingAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "clamd isn't answering.");
    }
}

// The models a process needs are published to blob storage (their meta.json is written last, so it marks a
// complete upload). Without the toxicity model nothing can be posted; without the nudity model no photo is published.
public sealed class ModelsHealthCheck(IBlobStorage storage, IReadOnlyList<string> prefixes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        foreach (var prefix in prefixes)
        {
            await using var meta = await storage.GetAsync($"{prefix}/meta.json", cancellationToken);
            if (meta is null)
            {
                missing.Add(prefix);
            }
        }
        return missing.Count == 0
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, $"Not published: {string.Join(", ", missing)}.");
    }
}

public static class HealthCheckBuilderExtensions
{
    extension(IHealthChecksBuilder builder)
    {
        public IHealthChecksBuilder AddPostgres() =>
            builder.AddCheck<PostgresHealthCheck>("postgres", HealthStatus.Unhealthy, [HealthTags.Ready]);

        public IHealthChecksBuilder AddRedis(HealthStatus failureStatus) =>
            builder.AddCheck<RedisHealthCheck>("redis", failureStatus, [HealthTags.Ready]);

        public IHealthChecksBuilder AddClamAv() =>
            builder.AddCheck<ClamAvHealthCheck>("clamav", HealthStatus.Unhealthy, [HealthTags.Ready]);
    }

    // A classic extension method: inside the extension block, params gets the wrong nullability at call sites (CS8620)
    public static IHealthChecksBuilder AddModels(this IHealthChecksBuilder builder, HealthStatus failureStatus, params string[] prefixes) =>
        builder.Add(new HealthCheckRegistration("models",
            services => new ModelsHealthCheck(services.GetRequiredService<IBlobStorage>(), prefixes), failureStatus, [HealthTags.Ready]));
}
