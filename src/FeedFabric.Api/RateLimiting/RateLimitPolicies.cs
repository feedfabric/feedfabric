using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;

namespace FeedFabric.Api.RateLimiting;

// Named policies for [EnableRateLimiting], each partitioned per user (RateLimitOptions)
public static class RateLimitPolicies
{
    public const string Posting = "posting";
    public const string Commenting = "commenting";
    public const string Reactions = "reactions";
    public const string Reports = "reports";
    public const string Uploads = "uploads";
    public const string Signals = "signals";
    public const string RankingPreview = "ranking-preview";

    public static IServiceCollection AddFeedFabricRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(limiter =>
        {
            limiter.OnRejected = OnRejectedAsync;
            Add(limiter, Posting, options.Posting, options.Enabled);
            Add(limiter, Commenting, options.Commenting, options.Enabled);
            Add(limiter, Reactions, options.Reactions, options.Enabled);
            Add(limiter, Reports, options.Reports, options.Enabled);
            Add(limiter, Uploads, options.Uploads, options.Enabled);
            Add(limiter, Signals, options.Signals, options.Enabled);
            Add(limiter, RankingPreview, options.RankingPreview, options.Enabled);
        });
        return services;
    }

    private static void Add(RateLimiterOptions limiter, string policy, RateLimitRule rule, bool enabled) =>
        limiter.AddPolicy(policy, http => enabled
            ? RateLimitPartition.GetFixedWindowLimiter(PartitionKey(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = rule.Window,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter(""));

    // The user when there is one; otherwise the caller's address
    private static string PartitionKey(HttpContext http) =>
        http.User.FindFirstValue(IdentityClaims.UserId) is { Length: > 0 } user
            ? $"user:{user}"
            : $"ip:{http.Connection.RemoteIpAddress}";

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        int? retryAfter = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
        {
            retryAfter = (int)Math.Ceiling(wait.TotalSeconds);
            http.Response.Headers.RetryAfter = retryAfter.Value.ToString(CultureInfo.InvariantCulture);
        }

        var result = ProblemResults.Create(http, StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited,
            "Too many requests; try again later.", retryAfter is null ? null : new { retryAfterSeconds = retryAfter });
        await result.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = http });
    }
}
