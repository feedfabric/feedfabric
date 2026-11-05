using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string ExternalUserId =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("No authenticated user on this request.");
}
