using System.Security.Claims;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string ExternalUserId =>
        // Whichever claim the host's tokens carry it in (AuthOptions.Claims.UserId), as IdentityClaims maps it
        httpContextAccessor.HttpContext?.User.FindFirstValue(IdentityClaims.UserId)
        ?? throw new InvalidOperationException("No authenticated user on this request.");
}
