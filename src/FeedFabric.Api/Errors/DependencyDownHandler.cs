using System.Net;
using Amazon.Runtime;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Application.Errors;
using Npgsql;
using StackExchange.Redis;

namespace FeedFabric.Api.Errors;

// When Postgres, Redis or blob storage can't be reached, a request that needs it is a 503 service_unavailable with
// Retry-After rather than a 500: the client did nothing wrong and can try again. The home feed doesn't get here
// without Redis (it reads Postgres instead); see docs/runbooks.md (Redis is down) for what each path does.
public sealed class DependencyDownHandler(ILogger<DependencyDownHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    private const int RetryAfterSeconds = 5;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (Dependency(exception) is not { } dependency || httpContext.Response.HasStarted)
        {
            return false;
        }
        logger.LogWarning(exception, "{Dependency} is unavailable; {Path} answered 503", dependency, httpContext.Request.Path);
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();
        var result = ProblemResults.Create(httpContext, StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServiceUnavailable,
            "A service this needs is unavailable; try again shortly.", new { retryAfterSeconds = RetryAfterSeconds });
        await result.ExecuteResultAsync(new ActionContext { HttpContext = httpContext });
        return true;
    }

    private static string? Dependency(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case RedisConnectionException or RedisTimeoutException:
                    return "Redis";
                case NpgsqlException { IsTransient: true }:
                    return "Postgres";
                case AmazonServiceException { StatusCode: >= HttpStatusCode.InternalServerError or HttpStatusCode.TooManyRequests }:
                case AmazonClientException:
                    return "Blob storage";
            }
        }
        return null;
    }
}
