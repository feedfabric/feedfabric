using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Users;
using StackExchange.Redis;

namespace FeedFabric.Application.Services.Ranking;

// Ranked rather than newest first, as a ranking profile says (RankingProfile). Candidates come from up to four
// places: the viewer's home timeline, recent popular posts on their topics, posts liked by people who like what they
// like, and posts close in meaning to their recent likes. Filters leave some out; the rest are scored by the trained
// model with the profile's goals, the formula (HeuristicRanker), the profile's own formula, or by age; freshness,
// boosts and "seen already" adjust the score; then DiversityReranker spreads authors and topics, mixes in accounts
// they don't follow and adds exploration slots. The order is kept in Redis for the session, so paging doesn't
// reshuffle it; the first page (no cursor) ranks again.
public class ForYouService(
    AppDbContext db,
    IConnectionMultiplexer redis,
    FeatureBuilder features,
    RankerModel model,
    FeedLog feedLog,
    EmbeddingIndex embeddingIndex,
    IMediaService media,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan SeenLookback = TimeSpan.FromHours(24);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HiddenLifetime = TimeSpan.FromDays(30);

    public async Task<ForYouResponse?> GetAsync(string externalUserId, ForYouQuery query, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (viewerId is null)
        {
            return null;
        }

        var offset = int.TryParse(query.Cursor, out var o) && o > 0 ? o : 0;
        var session = offset > 0 ? await LoadSessionAsync(viewerId.Value, profile) : null;
        if (session is null)
        {
            session = await RankAsync(viewerId.Value, profile, cancellationToken);
            await redis.GetDatabase().StringSetAsync(SessionKey(viewerId.Value, profile), JsonSerializer.Serialize(session), SessionLifetime);
        }

        var response = await PageAsync(session, offset, query.Limit, profile, cancellationToken);
        await feedLog.LogServedAsync(viewerId.Value, FeedNames.ForYou, session.Ranker,
            session.Posts.Skip(offset).Take(query.Limit).Select((p, i) => new ServedPost(p.PostId, offset + i, p.Score, p.Features, p.Explored, p.Propensity)).ToList(),
            cancellationToken, profile.Label);
        return response;
    }

    // What a profile would show this viewer right now, without logging it or touching their session: for trying out
    // a profile before saving it
    public async Task<ForYouResponse?> PreviewAsync(string externalUserId, ResolvedProfile profile, int limit, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (viewerId is null)
        {
            return null;
        }
        var session = await RankAsync(viewerId.Value, profile, cancellationToken);
        return await PageAsync(session, 0, limit, profile, cancellationToken);
    }

    private async Task<ForYouResponse> PageAsync(Session session, int offset, int limit, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var page = session.Posts.Skip(offset).Take(limit).ToList();
        var ids = page.Select(p => p.PostId).ToList();
        var posts = await db.Posts.Where(p => ids.Contains(p.Id) && p.DeletedAt == null).ToFeedPosts(db).ToDictionaryAsync(p => p.Id, cancellationToken);
        var results = page.Where(p => posts.ContainsKey(p.PostId)).Select((p, i) =>
        {
            var post = posts[p.PostId];
            post.Ranking = new PostRanking
            {
                Ranker = session.Ranker,
                Position = offset + i,
                Score = Math.Round(p.Score, 4),
                Explored = p.Explored,
                Factors = p.Factors,
                Features = RankingFeatures.ToDictionary(p.Features),
            };
            return post;
        }).ToList();
        await results.AttachMediaAsync(media, cancellationToken);

        var next = offset + limit;
        return new ForYouResponse
        {
            Posts = results,
            NextCursor = next < session.Posts.Count ? next.ToString() : null,
            Ranker = session.Ranker,
            Profile = profile.Label,
            Candidates = session.Posts.Count,
        };
    }

    // "Not interested": the post never comes back, and its author and topics count against it from now on
    public async Task<bool> NotInterestedAsync(string externalUserId, Guid postId, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        var authorId = await db.Posts.Where(p => p.Id == postId).Select(p => (Guid?)p.AuthorId).SingleOrDefaultAsync(cancellationToken);
        if (viewerId is null || authorId is null)
        {
            return false;
        }

        var tags = await db.PostTags.Where(t => t.PostId == postId).Select(t => new { t.Tag, t.Confidence }).ToListAsync(cancellationToken);
        var database = redis.GetDatabase();
        await database.SetAddAsync(HiddenKey(viewerId.Value), postId.ToString());
        await database.KeyExpireAsync(HiddenKey(viewerId.Value), HiddenLifetime);
        await database.SortedSetIncrementAsync(EngagementKeys.Affinity(viewerId.Value), authorId.Value.ToString(), -3);
        foreach (var tag in tags)
        {
            await database.SortedSetIncrementAsync(EngagementKeys.Topics(viewerId.Value), tag.Tag, -2 * tag.Confidence);
        }
        await feedLog.MarkAsync(viewerId.Value, postId, FeedSignal.NotInterested, cancellationToken);
        return true;
    }

    private async Task<Session> RankAsync(Guid viewerId, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var settings = profile.Settings;
        var candidates = await CandidatesAsync(viewerId, settings, cancellationToken);
        var signals = await features.BuildAsync(viewerId, candidates, cancellationToken);
        candidates = candidates.Where(c => ProfileRules.Passes(c, signals[c.Id], settings.Filters)).ToList();
        var rows = candidates.Select(c => signals[c.Id]).ToList();

        var (scores, ranker) = await ScoreAsync(rows, profile, cancellationToken);

        // Already seen in the last day: still eligible, but further down the list
        var since = timeProvider.GetUtcNow().UtcDateTime - SeenLookback;
        var candidateIds = candidates.Select(c => c.Id).ToList();
        var seen = settings.Variety.SeenPenalty >= 1
            ? []
            : (await db.FeedLog.Where(f => f.ViewerId == viewerId && f.Seen && f.ServedAt >= since && candidateIds.Contains(f.PostId))
                .Select(f => f.PostId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        var newest = settings.Scoring.Mode == ScoringMode.Newest;
        var byScore = candidates.Select((c, i) =>
        {
            var row = rows[i];
            List<RankingFactor> factors = newest
                ? [new RankingFactor("Newest first", 1, row[Age] < 60 ? $"{row[Age]:0} minutes old" : $"{row[Age] / 60:0.#} hours old")]
                : HeuristicRanker.Factors(row).ToList();
            if (profile.Expression is not null)
            {
                factors.Insert(0, new RankingFactor("Profile formula", scores[i], profile.Expression.Text));
            }
            var score = scores[i];

            void Apply(string label, double multiplier, string detail)
            {
                score = ProfileRules.Multiply(score, multiplier);
                factors.Add(new RankingFactor(label, multiplier, detail));
            }

            if (!newest && settings.Freshness.HalfLifeHours is { } halfLife)
            {
                Apply("Freshness", Math.Pow(0.5, row[Age] / 60 / halfLife), $"Halves every {halfLife:0.##} hours");
            }
            foreach (var boost in settings.Boosts.Where(b => ProfileRules.Matches(b, c, row)))
            {
                Apply(boost.Multiplier >= 1 ? "Boosted" : "Lowered", boost.Multiplier, ProfileRules.Describe(boost));
            }
            if (seen.Contains(c.Id))
            {
                Apply("Seen already", settings.Variety.SeenPenalty, "You've seen this in the last day");
            }
            var topic = c.Tags.FirstOrDefault(t => t.Source == "inferred")?.Tag ?? c.Tags.FirstOrDefault()?.Tag;
            return new DiversityReranker.Candidate(new RankedPost(c.Id, score, row, factors), c.AuthorId, topic, row[Follows] > 0);
        }).OrderByDescending(c => c.Post.Score).ToList();

        // Seeded per viewer and minute, so a refresh within the same minute explores the same posts
        var random = new Random(HashCode.Combine(viewerId, timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60));
        return new Session(ranker, DiversityReranker.Arrange(byScore, random, DiversityRules.From(settings)));
    }

    // Each post's score as the profile says, and what produced it for the feed log ("model:<version>", "heuristic",
    // "expression", "newest"). The model falls back to the formula until one is loaded.
    private async Task<(double[] Scores, string Ranker)> ScoreAsync(List<double[]> rows, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var scoring = profile.Settings.Scoring;
        if (scoring.Mode == ScoringMode.Newest)
        {
            return (rows.Select(r => 1 / (1 + r[Age])).ToArray(), "newest");
        }

        var needsModel = scoring.Mode == ScoringMode.Model || profile.Expression?.UsesModel == true;
        var useModel = needsModel && rows.Count > 0 && await model.EnsureLoadedAsync(cancellationToken);
        var formula = rows.Select(HeuristicRanker.Score).ToArray();
        var modelScores = useModel ? model.Score(rows, scoring.Goals.ToDictionary()) : formula;
        var modelRanker = useModel ? $"model:{model.Info!.Version}" : "heuristic";

        switch (scoring.Mode)
        {
            case ScoringMode.Model:
                return (modelScores, modelRanker);
            case ScoringMode.Expression when profile.Expression is { } expression:
                // Shifted to 0 or more, so multiplying it (by a decay, say) never lifts a post the model expects to be skipped
                var modelValues = useModel ? ProfileRules.NonNegativeModel(modelScores, scoring.Goals) : formula;
                var values = new double[ScoreExpression.Width];
                var scores = rows.Select((row, i) =>
                {
                    Array.Copy(row, values, row.Length);
                    values[ScoreExpression.IndexOf("age_hours")] = row[Age] / 60;
                    values[ScoreExpression.IndexOf("formula")] = formula[i];
                    values[ScoreExpression.IndexOf("model")] = modelValues[i];
                    return expression.Evaluate(values);
                }).ToArray();
                return (scores, expression.UsesModel ? $"expression+{modelRanker}" : "expression");
            default:
                return (formula, "heuristic");
        }
    }

    private async Task<List<HomeFeedPostResponse>> CandidatesAsync(Guid viewerId, RankingProfile profile, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        var sources = profile.Sources;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var since = now - TimeSpan.FromHours(sources.LookbackHours);
        var ids = new HashSet<Guid>();

        // 1. Who they follow
        if (sources.Following is { Enabled: true, Limit: > 0 })
        {
            var timeline = await database.SortedSetRangeByRankAsync(FeedKeys.ForUser(viewerId), 0, sources.Following.Limit - 1, Order.Descending);
            ids.UnionWith(timeline.Select(v => Guid.Parse(v.ToString())));
        }

        // 2. What's doing well right now on the topics they engage with most
        if (sources.Topics is { Enabled: true, Limit: > 0 })
        {
            var topTopics = (await database.SortedSetRangeByRankAsync(EngagementKeys.Topics(viewerId), 0, sources.TopTopics - 1, Order.Descending))
                .Select(v => v.ToString()).ToList();
            if (topTopics.Count > 0)
            {
                ids.UnionWith(await db.PostTags.Where(t => topTopics.Contains(t.Tag) && t.CreatedAt >= since)
                    .Select(t => t.PostId).Distinct()
                    .OrderByDescending(id => db.Likes.Count(l => l.PostId == id))
                    .Take(sources.Topics.Limit)
                    .ToListAsync(cancellationToken));
            }
        }

        // 3. People like you liked this: who else liked their recent likes, and what those people liked lately
        if (sources.SimilarReaders is { Enabled: true, Limit: > 0 })
        {
            ids.UnionWith(await db.Database.SqlQuery<Guid>($"""
                WITH mine AS (SELECT "PostId" FROM "Likes" WHERE "UserId" = {viewerId} ORDER BY "CreatedAt" DESC LIMIT 50),
                     peers AS (SELECT "UserId", count(*) AS shared FROM "Likes"
                               WHERE "PostId" IN (SELECT "PostId" FROM mine) AND "UserId" <> {viewerId}
                               GROUP BY "UserId" ORDER BY shared DESC LIMIT 100)
                SELECT l."PostId" AS "Value" FROM "Likes" l JOIN peers p ON p."UserId" = l."UserId"
                WHERE l."CreatedAt" >= {since} AND l."PostId" NOT IN (SELECT "PostId" FROM mine)
                GROUP BY l."PostId" ORDER BY sum(p.shared) DESC LIMIT {sources.SimilarReaders.Limit}
                """).ToListAsync(cancellationToken));
        }

        // 4. Close in meaning to what they like, whoever posted it
        if (sources.SimilarMeaning is { Enabled: true, Limit: > 0 })
        {
            var viewerVector = await features.ViewerVectorAsync(viewerId, cancellationToken);
            if (viewerVector is not null)
            {
                var similar = await embeddingIndex.NearestAsync(viewerVector, sources.SimilarMeaning.Limit, ids, cancellationToken);
                ids.UnionWith(similar.Select(s => s.PostId));
            }
        }

        // Reposts in the timeline stand for the posts they share
        var reposted = await db.Posts.Where(p => ids.Contains(p.Id) && p.RepostOfId != null)
            .Select(p => p.RepostOfId!.Value).ToListAsync(cancellationToken);
        ids.UnionWith(reposted);

        var hidden = (await database.SetMembersAsync(HiddenKey(viewerId))).Select(v => Guid.Parse(v.ToString())).ToHashSet();
        ids.ExceptWith(hidden);

        var oldest = profile.Freshness.MaxAgeHours is { } maxAge ? now - TimeSpan.FromHours(maxAge) : DateTime.MinValue;
        return await db.Posts.Where(p => ids.Contains(p.Id) && p.DeletedAt == null && p.RepostOfId == null && p.AuthorId != viewerId && p.CreatedAt >= oldest)
            .ToFeedPosts(db).ToListAsync(cancellationToken);
    }

    private async Task<Session?> LoadSessionAsync(Guid viewerId, ResolvedProfile profile)
    {
        var json = await redis.GetDatabase().StringGetAsync(SessionKey(viewerId, profile));
        return json.HasValue ? JsonSerializer.Deserialize<Session>(json.ToString()) : null;
    }

    // Per profile version, so switching profiles (or saving a new version) never pages through another one's order
    private static string SessionKey(Guid viewerId, ResolvedProfile profile) => $"foryou:{viewerId}:{profile.Label}";

    private static string HiddenKey(Guid viewerId) => $"hidden:{viewerId}";

    private static readonly int Age = RankingFeatures.Index("age_minutes");
    private static readonly int Follows = RankingFeatures.Index("follows_author");

    private sealed record Session(string Ranker, List<RankedPost> Posts);
}

public class ForYouQuery
{
    // The previous page's NextCursor
    public string? Cursor { get; set; }

    [Range(1, 100, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")]
    public int Limit { get; set; } = 20;

    // A ranking profile's key; the configured default when left out
    public string? Profile { get; set; }
}

public class ForYouResponse
{
    public required IReadOnlyList<HomeFeedPostResponse> Posts { get; set; }

    public string? NextCursor { get; set; }

    // What scored it: "heuristic", "model:<version>", "expression" (or "expression+model:<version>") or "newest"
    public required string Ranker { get; set; }

    // The ranking profile, as "<key>" for a preset or "<key>@<version>"
    public required string Profile { get; set; }

    // How many posts were ranked for this session
    public int Candidates { get; set; }
}

// Why a For You post is where it is: its score, the formula's parts in words, and the raw signals
public class PostRanking
{
    public required string Ranker { get; set; }

    public int Position { get; set; }

    public double Score { get; set; }

    // In an exploration slot: shown to learn about it, not because it scored highest
    public bool Explored { get; set; }

    public required IReadOnlyList<RankingFactor> Factors { get; set; }

    public required IReadOnlyDictionary<string, double> Features { get; set; }
}
