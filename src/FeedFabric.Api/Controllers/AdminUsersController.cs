using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Api.Hosting;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Admin;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

public class UserStatusRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [AllowedValues("active", "suspended", ErrorMessage = ErrorCodes.InvalidValue)]
    public string Status { get; set; } = "";
}

public class UserVerifiedRequest
{
    public bool Verified { get; set; }
}

// Users for moderators: search, profile, suspend, verify and delete
[ApiController]
[Authorize(Policy = ModerationPolicies.Reviewer)]
[Route("admin/users")]
public class AdminUsersController(AdminUserService users, AccountService accounts, ICurrentUser currentUser, IMemoryCache cache,
    ILogger<AdminUsersController> logger) : ControllerBase
{
    // GET /admin/users?q=&status=active|suspended&sort=username|posts|reports|flags&offset=0&limit=50
    [HttpGet]
    public Task<AdminPage<AdminUserRow>> Search(string? q, UserStatus? status, AdminUserSort sort = AdminUserSort.Username,
        [Range(0, int.MaxValue)] int offset = 0, [Range(1, 200, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")] int limit = 50,
        CancellationToken cancellationToken = default) =>
        users.SearchAsync(q, status, sort, offset, limit, cancellationToken);

    // GET /admin/users/{externalUserId}
    [HttpGet("{externalUserId}")]
    public async Task<IActionResult> Detail(string externalUserId, CancellationToken cancellationToken) =>
        await users.DetailAsync(externalUserId, cancellationToken) is { } detail ? Ok(detail) : NotFoundUser(externalUserId);

    // PUT /admin/users/{externalUserId}/status { "status": "suspended" | "active" }: recorded in the moderation log
    [HttpPut("{externalUserId}/status")]
    public async Task<IActionResult> SetStatus(string externalUserId, UserStatusRequest request, CancellationToken cancellationToken)
    {
        var status = request.Status == "suspended" ? UserStatus.Suspended : UserStatus.Active;
        if (!await users.SetStatusAsync(externalUserId, status, currentUser.ExternalUserId, cancellationToken))
        {
            return NotFoundUser(externalUserId);
        }
        SuspendedUsers.Forget(cache, externalUserId);
        return NoContent();
    }

    // PUT /admin/users/{externalUserId}/verified { "verified": true }
    [HttpPut("{externalUserId}/verified")]
    public async Task<IActionResult> SetVerified(string externalUserId, UserVerifiedRequest request, CancellationToken cancellationToken) =>
        await users.SetVerifiedAsync(externalUserId, request.Verified, cancellationToken) ? NoContent() : NotFoundUser(externalUserId);

    // DELETE /admin/users/{externalUserId}: an erasure request that came another way (support, the host's own
    // account deletion); the same as the member's DELETE /me
    [HttpDelete("{externalUserId}")]
    public async Task<IActionResult> Delete(string externalUserId, CancellationToken cancellationToken)
    {
        if (!await accounts.DeleteAsync(externalUserId, cancellationToken))
        {
            return NotFoundUser(externalUserId);
        }
        logger.LogInformation("{Staff} deleted the account of {ExternalUserId}", LogValue.Of(currentUser.ExternalUserId), LogValue.Of(externalUserId));
        AccountController.Forget(cache, externalUserId);
        return Accepted();
    }

    private ObjectResult NotFoundUser(string externalUserId) =>
        this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound, "No such user.", new { externalUserId });
}
