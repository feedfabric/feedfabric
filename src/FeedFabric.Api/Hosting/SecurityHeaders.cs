namespace FeedFabric.Api.Hosting;

// Headers on every response. The API only returns JSON (and, for local blob storage, files), so nothing it sends
// should ever run as a page, be framed or be sniffed into something else. A handler that sets its own (the local
// blob endpoint's "sandbox" CSP) keeps it.
public sealed class SecurityHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            // Responses carry members' own data: never kept by a shared cache
            if (!headers.ContainsKey("Cache-Control"))
            {
                headers.CacheControl = "no-store";
            }
            return Task.CompletedTask;
        });
        return next(context);
    }
}
