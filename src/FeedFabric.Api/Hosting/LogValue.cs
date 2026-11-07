namespace FeedFabric.Api.Hosting;

// For a value from the request that goes into a log message: a path, or an ID from the URL or a token. Line breaks
// are replaced, so a crafted value can't start what looks like a second log line. The JSON console formatter escapes
// them anyway; the plain-text one used in Development, and whatever reads the logs, may not.
public static class LogValue
{
    public static string? Of(string? value) =>
        value?.Replace('\r', '_').Replace('\n', '_');
}
