using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Api.Auth;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Storage;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

// For the admin panel (web/admin): who's signed in, and how the service is set up
[ApiController]
[Route("admin")]
public class AdminController(IHostEnvironment environment) : ControllerBase
{
    private static readonly string[] ModelPrefixes = ["models/safety/toxicity", "models/safety/nsfw", "models/embeddings/minilm"];

    // GET /admin/me: any signed-in user, so the panel can say what they can and can't do rather than fail
    [HttpGet("me")]
    [Authorize]
    public async Task<object> Me(AppDbContext db, CancellationToken cancellationToken)
    {
        var externalUserId = User.FindFirst(IdentityClaims.UserId)?.Value ?? "";
        var user = await db.Users.AsNoTracking().Where(u => u.ExternalUserId == externalUserId)
            .Select(u => new { u.Username, u.DisplayName }).SingleOrDefaultAsync(cancellationToken);
        var development = environment.IsDevelopment();
        return new
        {
            ExternalUserId = externalUserId,
            user?.Username,
            user?.DisplayName,
            Moderator = development || User.HasClaim(IdentityClaims.Role, ModerationPolicies.ReviewerRole),
            RankingAdmin = development || User.HasClaim(IdentityClaims.Role, RankingPolicies.AdminRole),
            Environment = environment.EnvironmentName,
        };
    }

    // GET /admin/system: sign-in, moderation settings, models and the event pipeline. No secret values: a signing key
    // only shows as set or not.
    [HttpGet("system")]
    [Authorize(Policy = AdminPolicies.Staff)]
    public async Task<object> System(AppDbContext db, IBlobStorage storage, RankerModel ranker, IOptions<AuthOptions> auth,
        IOptions<ModerationOptions> moderation, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var models = new List<object>();
        foreach (var prefix in ModelPrefixes)
        {
            await using var file = await storage.GetAsync($"{prefix}/meta.json", cancellationToken);
            if (file is null)
            {
                models.Add(new { Key = prefix, Present = false });
                continue;
            }
            using var meta = await JsonDocument.ParseAsync(file.Content, cancellationToken: cancellationToken);
            var root = meta.RootElement;
            models.Add(new
            {
                Key = prefix,
                Present = true,
                Kind = Text(root, "kind"),
                Source = Text(root, "source"),
                TunedAt = Text(root, "tunedAt"),
                Labels = root.TryGetProperty("labels", out var labels) ? labels.GetArrayLength() : (int?)null,
                Tests = root.TryGetProperty("tests", out var tests) ? tests.Clone() : (JsonElement?)null,
            });
        }
        await ranker.EnsureLoadedAsync(cancellationToken);

        var a = auth.Value;
        var m = moderation.Value;
        var oldestWaiting = await db.OutboxMessages.Where(o => o.ProcessedAt == null).MinAsync(o => (DateTime?)o.CreatedAt, cancellationToken);
        return new
        {
            Environment = environment.EnvironmentName,
            Auth = new
            {
                a.Mode,
                a.Jwt.Authority,
                a.Jwt.Audience,
                a.Jwt.Issuer,
                SigningKeySet = !string.IsNullOrEmpty(a.Jwt.SigningKey),
                Claims = new
                {
                    a.Claims.UserId, Username = a.Claims.UsernameOrDefault, DisplayName = a.Claims.DisplayNameOrDefault, a.Claims.Avatar,
                    Roles = a.Claims.RolesOrDefault,
                },
                Roles = new { Moderator = a.Roles.ModeratorOrDefault, RankingAdmin = a.Roles.RankingAdminOrDefault },
                a.ProvisionUsers,
            },
            Moderation = new
            {
                BlockAt = m.ToxicityRejectAt,
                HoldAt = m.ToxicityHoldAt,
                m.ReportsToHold,
                m.NudityRejectAt,
                m.MaxLinks,
                m.MaxRepeatedCharacters,
                BlockedTerms = m.BlockedTerms.Count,
            },
            Models = models,
            Ranker = ranker.Info is { } info
                ? new { Loaded = true, info.Version, info.TrainedAt, info.TrainingRows }
                : (object)new { Loaded = false },
            Users = await db.Users.GroupBy(u => u.Status).Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Status.ToString().ToLowerInvariant(), g => g.Count, cancellationToken),
            Pipeline = new
            {
                OutboxWaiting = await db.OutboxMessages.CountAsync(o => o.ProcessedAt == null, cancellationToken),
                OldestWaitingSeconds = oldestWaiting is null ? (double?)null : Math.Round((now - oldestWaiting.Value).TotalSeconds),
                Consumers = await db.ProcessedEvents.GroupBy(e => e.Consumer)
                    .Select(g => new { Consumer = g.Key, LastProcessedAt = g.Max(e => e.ProcessedAt) })
                    .OrderBy(c => c.Consumer)
                    .ToListAsync(cancellationToken),
            },
        };
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
