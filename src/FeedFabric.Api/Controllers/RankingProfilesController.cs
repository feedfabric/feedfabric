using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// Ranking profiles: how the For You feed is put together, as named, versioned settings. Presets are read-only;
// saving under a new key makes your own, and saving again adds a version. Changes the platform's feed, so only
// ranking admins (RankingPolicies.Admin).
[ApiController]
[Authorize(Policy = RankingPolicies.Admin)]
[Route("ranking")]
public class RankingProfilesController(RankingProfileStore store, ForYouService forYou, ICurrentUser currentUser) : ControllerBase
{
    private const int MaxPreview = 50;

    // GET /ranking/profiles: presets first, then saved profiles at their latest version
    [HttpGet("profiles")]
    public Task<List<ProfileSummary>> List(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    [HttpGet("profiles/{key}")]
    public async Task<ActionResult<ProfileDetail>> Get(string key, CancellationToken cancellationToken)
    {
        var profile = await store.GetAsync(key, cancellationToken);
        return profile is null ? NotFoundProblem(key) : Detail(profile);
    }

    [HttpGet("profiles/{key}/versions")]
    public async Task<ActionResult<List<ProfileVersion>>> Versions(string key, CancellationToken cancellationToken)
    {
        if (RankingPresets.IsPreset(key))
        {
            return new List<ProfileVersion>();
        }
        var versions = await store.VersionsAsync(key, cancellationToken);
        return versions.Count == 0 ? NotFoundProblem(key) : versions;
    }

    [HttpGet("profiles/{key}/versions/{version:int}")]
    public async Task<ActionResult<ProfileDetail>> GetVersion(string key, int version, CancellationToken cancellationToken)
    {
        var profile = await store.GetVersionAsync(key, version, cancellationToken);
        return profile is null ? NotFoundProblem(key) : Detail(profile);
    }

    // PUT /ranking/profiles/{key}: creates the profile, or saves a new version of it. Feeds use it from their next
    // first page.
    [HttpPut("profiles/{key}")]
    public async Task<ActionResult<ProfileDetail>> Save(string key, RankingProfile profile, CancellationToken cancellationToken)
    {
        var result = await store.SaveAsync(key, profile, currentUser.ExternalUserId, cancellationToken);
        return result.Status switch
        {
            ProfileSaveStatus.Ok => Detail(result.Profile!),
            ProfileSaveStatus.ReadOnly => this.ApiProblem(StatusCodes.Status409Conflict, ErrorCodes.ProfileReadOnly,
                $"'{key}' is a preset and can't be changed. Save it under a new name.", new { profile = key }),
            ProfileSaveStatus.InvalidKey => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.ProfileInvalidKey,
                "Use 2-40 lowercase letters, digits and dashes, starting with a letter.", new { profile = key }),
            _ => InvalidProblem(result.Errors!),
        };
    }

    [HttpDelete("profiles/{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken cancellationToken)
    {
        if (RankingPresets.IsPreset(key))
        {
            return this.ApiProblem(StatusCodes.Status409Conflict, ErrorCodes.ProfileReadOnly, $"'{key}' is a preset and can't be deleted.", new { profile = key });
        }
        return await store.DeleteAsync(key, cancellationToken) ? NoContent() : NotFoundProblem(key);
    }

    // POST /ranking/preview?viewer=<externalUserId>&limit=30: the feed these settings would give, without saving
    // anything or logging it. viewer defaults to you.
    [HttpPost("preview")]
    public async Task<ActionResult<ForYouResponse>> Preview(RankingProfile profile, [FromQuery] string? viewer, [FromQuery] int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var draft = RankingProfileStore.Draft(profile, out var errors);
        if (draft is null)
        {
            return InvalidProblem(errors);
        }
        var externalUserId = string.IsNullOrWhiteSpace(viewer) ? currentUser.ExternalUserId : viewer;
        var feed = await forYou.PreviewAsync(externalUserId, draft, Math.Clamp(limit, 1, MaxPreview), cancellationToken);
        return feed is null
            ? this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.UserNotFound, $"User {externalUserId} not found.", new { externalUserId })
            : feed;
    }

    // GET /ranking/vocabulary: everything a profile can use, in words, for editors and docs
    [HttpGet("vocabulary")]
    public object Vocabulary() => new
    {
        Variables = ScoreExpression.Variables,
        Functions = ScoreExpression.Functions.Select(f => new { f.Name, f.Signature, f.Description }),
        Examples = ScoreExpression.Examples,
        Operators = new[] { "+ - * / ^", "> < >= <= == != (1 if true, 0 if not)", "and or not", "( )" },
        Goals = new[]
        {
            new { Name = "open", Description = "Opens the post" },
            new { Name = "like", Description = "Likes it" },
            new { Name = "comment", Description = "Comments on it" },
            new { Name = "longRead", Description = "Stays on it for 10 seconds or more" },
            new { Name = "skip", Description = "Scrolls past it" },
        },
        Presets = RankingPresets.All.Select(p => new { Key = p.Key, Settings = p.Value() }),
        Defaults = new RankingProfile(),
        DefaultProfile = store.DefaultKey,
    };

    private ProfileDetail Detail(ResolvedProfile profile) =>
        new(profile.Key, profile.Version, profile.IsPreset, profile.Label, profile.Key == store.DefaultKey, profile.Settings);

    private ObjectResult NotFoundProblem(string key) =>
        this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.ProfileNotFound, $"There's no ranking profile called '{key}'.", new { profile = key });

    private ObjectResult InvalidProblem(IReadOnlyList<ProfileError> errors) =>
        ProblemResults.Create(HttpContext, StatusCodes.Status400BadRequest, ErrorCodes.ProfileInvalid,
            "Some settings need changing.", errors: errors);
}

public sealed record ProfileDetail(string Key, int Version, bool Preset, string Label, bool IsDefault, RankingProfile Settings);
