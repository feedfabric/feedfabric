using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Application.Services.Storage;
using Npgsql;
using StackExchange.Redis;

namespace FeedFabric.Worker.Events.Kafka;

// Failures that say something the handler needs is down, rather than that the event is bad. Retrying those for as
// long as it takes is right: dead-lettering every event while Postgres restarts would only make work.
public static class TransientFailures
{
    public static bool IsTransient(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is MalwareScanException or ModelNotFoundException or RedisConnectionException or RedisTimeoutException
                or SocketException or TimeoutException or HttpRequestException
                || ex is NpgsqlException { IsTransient: true }
                // S3 throttling or a server error, after the SDK's own retries, or no answer at all
                || ex is AmazonServiceException { StatusCode: >= HttpStatusCode.InternalServerError or HttpStatusCode.TooManyRequests }
                    or AmazonServiceException { ErrorCode: "SlowDown" or "Throttling" or "RequestTimeout" }
                || ex is AmazonClientException)
            {
                return true;
            }
        }
        return false;
    }
}
