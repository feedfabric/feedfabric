using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.ApiKeys;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

public class CreateApiKeyRequest
{
    // What it's for, e.g. "Web app backend"
    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(ApiKeyStore.MaxNameLength, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string Name { get; init; } = "";
}

// API keys for the host's own servers, which use them through the SDKs (sdks/) to act for their members. The key
// itself is only in the response that makes it.
[ApiController]
[Authorize(Policy = AdminPolicies.IntegrationAdmin)]
[Route("admin/api-keys")]
public class AdminApiKeysController(ApiKeyStore store, ICurrentUser currentUser, IMemoryCache cache) : ControllerBase
{
    // GET /admin/api-keys: newest first, revoked ones included
    [HttpGet]
    public Task<List<ApiKeySummary>> List(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    // POST /admin/api-keys { "name": "..." }: 201 with the summary and "key", which is never shown again
    [HttpPost]
    public async Task<IActionResult> Create(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var created = await store.CreateAsync(request.Name, currentUser.ExternalUserId, cancellationToken);
        var s = created.Summary;
        return StatusCode(StatusCodes.Status201Created, new
        {
            s.Id, s.Name, s.Prefix, s.CreatedAt, s.CreatedBy, s.LastUsedAt, s.RevokedAt, s.RevokedBy, created.Key,
        });
    }

    // DELETE /admin/api-keys/{id}: revokes it. At once on this server, within a minute on the others.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (await store.RevokeAsync(id, currentUser.ExternalUserId, cancellationToken) is not { } hash)
        {
            return this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.ApiKeyNotFound, "No such API key.");
        }
        ApiKeyAuthenticationHandler.Forget(cache, hash);
        return NoContent();
    }
}
