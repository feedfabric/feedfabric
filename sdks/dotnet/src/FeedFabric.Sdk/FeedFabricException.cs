using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FeedFabric.Sdk;

/// <summary>
/// An API error. Every one has a stable <see cref="Code"/> (docs/errors.md): match on it, not on the message.
/// </summary>
/// <example>
/// <code>catch (FeedFabricException e) when (e.Code == "post.not_found") { ... }</code>
/// </example>
public sealed class FeedFabricException(
    int status,
    string code,
    string message,
    IReadOnlyDictionary<string, JsonElement>? parameters = null,
    IReadOnlyList<JsonElement>? errors = null,
    string? traceId = null,
    TimeSpan? retryAfter = null,
    Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The HTTP status; 0 when the request never got a response.</summary>
    public int Status { get; } = status;

    /// <summary>e.g. "post.not_found", "content.violation", "rate_limited"; "network_error" without a response.</summary>
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, JsonElement> Params { get; } = parameters ?? new Dictionary<string, JsonElement>();

    /// <summary>validation_failed lists fields; post.data_invalid lists {"path", "message"}.</summary>
    public IReadOnlyList<JsonElement> Errors { get; } = errors ?? [];

    public string? TraceId { get; } = traceId;

    /// <summary>From Retry-After on 429 and 503.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    internal static FeedFabricException From(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        Problem? problem = null;
        try
        {
            problem = JsonSerializer.Deserialize<Problem>(body, FeedFabricJson.Options);
        }
        catch (JsonException)
        {
            // Not Problem Details: a proxy or load balancer answered
        }
        return new FeedFabricException(status,
            problem?.Code ?? $"http_{status}",
            problem?.Detail ?? $"{status} {response.ReasonPhrase}",
            problem?.Params, problem?.Errors, problem?.TraceId, RetryAfterOf(response.Headers.RetryAfter));
    }

    private static TimeSpan? RetryAfterOf(RetryConditionHeaderValue? header) => header switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
        _ => null,
    };

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Status} {Code}: {Message}");

    private sealed record Problem(string? Code, string? Detail, Dictionary<string, JsonElement>? Params, List<JsonElement>? Errors,
        string? TraceId);
}
