using FeedFabric.Api.Auth;

namespace FeedFabric.Api.Hosting;

// The audit log of staff actions: every change made through /admin or /ranking (suspending, verifying or deleting a
// user, a moderation decision, saving or deleting a ranking profile, setting the default), who made it and how it
// ended, as a structured log event in the "FeedFabric.Audit" category. Ship that category to storage staff can't
// edit (docs/runbooks.md). The moderation log and profile versions keep their own record of who decided what.
public sealed class StaffAudit(RequestDelegate next, ILoggerFactory loggers)
{
    private readonly ILogger logger = loggers.CreateLogger("FeedFabric.Audit");

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!IsStaffChange(request))
        {
            await next(context);
            return;
        }
        try
        {
            await next(context);
        }
        finally
        {
            var user = context.User;
            logger.LogInformation("Staff action {Method} {Path} by {Actor} (roles {Roles}) from {RemoteIp}: {StatusCode}",
                request.Method, request.Path.Value, user.FindFirst(IdentityClaims.UserId)?.Value ?? "(signed out)",
                string.Join(",", user.FindAll(IdentityClaims.Role).Select(c => c.Value)), context.Connection.RemoteIpAddress,
                context.Response.StatusCode);
        }
    }

    // Writes only; POST /ranking/preview changes nothing
    private static bool IsStaffChange(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
        && (request.Path.StartsWithSegments("/admin") || request.Path.StartsWithSegments("/ranking"))
        && !request.Path.StartsWithSegments("/ranking/preview");
}
