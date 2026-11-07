using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Api.RateLimiting;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// The community's topics: what posts are tagged with from their wording, which For You matches to readers. Staff
// choose a set from the library on first use and adjust it from there; every save is a new version. Changes every
// feed, so only ranking admins (RankingPolicies.Admin).
[ApiController]
[Authorize(Policy = RankingPolicies.Admin)]
[Route("admin/topics")]
public class AdminTopicsController(TopicStore store, ICurrentUser currentUser) : ControllerBase
{
    // GET /admin/topics: the set in use and where it comes from; "default" means nobody has chosen yet
    [HttpGet]
    public async Task<TopicsResponse> Get(CancellationToken cancellationToken) =>
        TopicsResponse.From(await store.CurrentAsync(cancellationToken));

    // GET /admin/topics/library: ready-made sets to start from
    [HttpGet("library")]
    public IReadOnlyList<TopicLibrarySet> Library() => TopicLibrary.All;

    // GET /admin/topics/versions: every saved set, newest first
    [HttpGet("versions")]
    public Task<List<TopicVersion>> Versions(CancellationToken cancellationToken) => store.VersionsAsync(cancellationToken);

    [HttpGet("versions/{version:int}")]
    public async Task<ActionResult<Dictionary<string, List<string>>>> GetVersion(int version, CancellationToken cancellationToken) =>
        await store.VersionAsync(version, cancellationToken) is { } topics
            ? Ok(topics)
            : this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.TopicsVersionNotFound, $"There's no saved topic set {version}.", new { version });

    // PUT /admin/topics: saves the whole set as a new version. New and edited posts use it within a minute; posts
    // already tagged keep their tags.
    [HttpPut]
    public async Task<ActionResult<TopicsResponse>> Save(TopicsRequest request, CancellationToken cancellationToken)
    {
        var result = await store.SaveAsync(request.Topics, request.Library, currentUser.ExternalUserId, cancellationToken);
        return result.Status switch
        {
            TopicSaveStatus.Ok => TopicsResponse.From(result.Topics!),
            TopicSaveStatus.UnknownLibrary => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.TopicsUnknownLibrary,
                $"There's no library set called '{request.Library}'.", new { library = request.Library }),
            _ => ProblemResults.Create(HttpContext, StatusCodes.Status400BadRequest, ErrorCodes.TopicsInvalid,
                "Some topics need changing.", errors: result.Errors),
        };
    }

    // POST /admin/topics/preview: the topics some text gets, from a draft set or the one in use
    [EnableRateLimiting(RateLimitPolicies.RankingPreview)]
    [HttpPost("preview")]
    public async Task<ActionResult<List<TopicMatchResponse>>> Preview(TopicPreviewRequest request, CancellationToken cancellationToken)
    {
        TopicMatcher matcher;
        if (request.Topics is { } draft)
        {
            var errors = TopicMatcher.Problems(draft);
            if (errors.Count > 0)
            {
                return ProblemResults.Create(HttpContext, StatusCodes.Status400BadRequest, ErrorCodes.TopicsInvalid,
                    "Some topics need changing.", errors: errors);
            }
            matcher = new TopicMatcher(TopicMatcher.Normalise(draft));
        }
        else
        {
            matcher = (await store.CurrentAsync(cancellationToken)).Matcher;
        }
        return matcher.Match(request.Text).OrderByDescending(m => m.Confidence).Select(m => new TopicMatchResponse(m.Topic, m.Confidence)).ToList();
    }
}
