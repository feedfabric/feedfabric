using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Auth;

// A suspended user can still read, but can't post, comment, like, repost, follow, report or upload: every write is
// refused here, in one place, with user.suspended. Staff routes are left alone. A user's status is cached briefly, so
// a suspension takes effect within StatusCacheTime.
public sealed class SuspendedUsers(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan StatusCacheTime = TimeSpan.FromSeconds(30);

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var externalUserId = context.User.FindFirst(IdentityClaims.UserId)?.Value;
        if (externalUserId is not null && IsWrite(context.Request) && !IsStaffRoute(context.Request.Path) && !IsReadingSignal(context.Request.Path)
            && !IsAccountDeletion(context.Request)
            && await StatusAsync(db, externalUserId, context.RequestAborted) == UserStatus.Suspended)
        {
            var problem = ProblemResults.Create(context, StatusCodes.Status403Forbidden, ErrorCodes.UserSuspended,
                "This account is suspended, so it can't post or interact for now.");
            await problem.ExecuteResultAsync(new ActionContext { HttpContext = context });
            return;
        }
        await next(context);
    }

    // Called when staff change a user's status, so it takes effect at once on this server
    public static void Forget(IMemoryCache cache, string externalUserId) => cache.Remove(CacheKey(externalUserId));

    private async Task<UserStatus?> StatusAsync(AppDbContext db, string externalUserId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey(externalUserId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StatusCacheTime;
            return db.Users.Where(u => u.ExternalUserId == externalUserId).Select(u => (UserStatus?)u.Status).SingleOrDefaultAsync(cancellationToken);
        });

    private static bool IsWrite(HttpRequest request) => !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);

    // What the client reports while its user reads (ImpressionsController): part of reading, so allowed
    private static bool IsReadingSignal(PathString path) =>
        path.StartsWithSegments("/userfeed/impressions") || path.StartsWithSegments("/userfeed/dwell")
        || (path.StartsWithSegments("/userfeed/posts", out var rest)
            && rest.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, "open" or "not-interested" or "show-more"]);

    // Suspended or not, members can delete their account
    private static bool IsAccountDeletion(HttpRequest request) => HttpMethods.IsDelete(request.Method) && request.Path == "/me";

    private static bool IsStaffRoute(PathString path) => path.StartsWithSegments("/admin") || path.StartsWithSegments("/ranking");

    private static string CacheKey(string externalUserId) => $"status:{externalUserId}";
}
