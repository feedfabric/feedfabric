using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Api.RateLimiting;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// The signed-in member's own account: everything held about them, and deleting it
[ApiController]
[Authorize]
[Route("me")]
[EnableRateLimiting(RateLimitPolicies.Account)]
public class AccountController(AccountService accounts, ICurrentUser currentUser, IMemoryCache cache) : ControllerBase
{
    // GET /me/export: one JSON document, the feed log streamed at the end
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        if (await accounts.ExportAsync(currentUser.ExternalUserId, cancellationToken) is not { } export)
        {
            return NotFoundUser();
        }
        Response.Headers.ContentDisposition = "attachment; filename=\"feedfabric-export.json\"";
        return Ok(export);
    }

    // DELETE /me: posts, comments and profile go at once; the worker erases the rest shortly after. 202 because of
    // that second part. Allowed while suspended.
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        if (!await accounts.DeleteAsync(currentUser.ExternalUserId, cancellationToken))
        {
            return NotFoundUser();
        }
        Forget(cache, currentUser.ExternalUserId);
        return Accepted();
    }

    // On this server at once; other servers notice within their cache times (AuthOptions.ProfileRefresh at most),
    // and until then the old token reaches no account
    internal static void Forget(IMemoryCache cache, string externalUserId)
    {
        UserProvisioning.Forget(cache, externalUserId);
        SuspendedUsers.Forget(cache, externalUserId);
    }

    private ObjectResult NotFoundUser() =>
        this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound, "No such user.", new { currentUser.ExternalUserId });
}
