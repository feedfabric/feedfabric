using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Embeddings;
using FeedFabric.Application.Services.Favourites;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Safety;
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

        var offset = Offset(query);
        var session = await SessionAsync(viewerId.Value, offset, profile, SessionKey(viewerId.Value, profile), cancellationToken);
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

    // What this viewer's For You shows, paged like theirs but in a session of its own and without logging it, so
    // looking neither becomes training data nor reshuffles the order they're paging through: for staff
    public async Task<ForYouResponse?> PeekAsync(string externalUserId, ForYouQuery query, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (viewerId is null)
        {
            return null;
        }
        var offset = Offset(query);
        var session = await SessionAsync(viewerId.Value, offset, profile, PeekKey(viewerId.Value, profile), cancellationToken);
        return await PageAsync(session, offset, query.Limit, profile, cancellationToken);
    }

    private static int Offset(ForYouQuery query) => int.TryParse(query.Cursor, out var o) && o > 0 ? o : 0;

    // The first page ranks again; later pages keep the order kept under key, or rank again if it has expired
    private async Task<Session> SessionAsync(Guid viewerId, int offset, ResolvedProfile profile, string key, CancellationToken cancellationToken)
    {
        var session = offset > 0 ? await LoadSessionAsync(key) : null;
        if (session is null)
        {
            session = await RankAsync(viewerId, profile, cancellationToken);
            await redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(session), SessionLifetime);
        }
        return session;
    }

    private async Task<ForYouResponse> PageAsync(Session session, int offset, int limit, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var page = session.Posts.Skip(offset).Take(limit).ToList();
        var ids = page.Select(p => p.PostId).ToList();
        // Visible only: a post can be held, or its author suspended, during a session
        var posts = await db.Posts.Where(p => ids.Contains(p.Id) && p.DeletedAt == null && p.Status == ContentStatus.Visible
                && db.Users.Any(u => u.Id == p.AuthorId && u.Status == UserStatus.Active))
            .ToFeedPosts(db).ToDictionaryAsync(p => p.Id, cancellationToken);
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
    public Task<bool> NotInterestedAsync(string externalUserId, Guid postId, CancellationToken cancellationToken) =>
        FeedbackAsync(externalUserId, postId, Feedback.Less, cancellationToken);

    // "Show more": the opposite. Its author and topics count for it from now on, as much as "Not interested" counts
    // against, which is more than a like; and if it was hidden, it can come back.
    public Task<bool> ShowMoreAsync(string externalUserId, Guid postId, CancellationToken cancellationToken) =>
        FeedbackAsync(externalUserId, postId, Feedback.More, cancellationToken);

    // A viewer's feedback on a post is a state, not a counter: pressing the same button again changes nothing, and
    // switching from one to the other first undoes the earlier one. Every key it touches is the viewer's own and they
    // change in one transaction, so a failure never leaves half of it applied. On a Redis cluster those keys must share
    // a slot, which they don't yet: putting each viewer's keys under one hash tag is part of making it shardable
    // (docs/production-readiness.md).
    private async Task<bool> FeedbackAsync(string externalUserId, Guid postId, Feedback feedback, CancellationToken cancellationToken)
    {
        var viewerId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        // "Show more" only on posts everyone can see, so a boost doesn't linger for a removed post that comes back.
        // "Not interested" on any post: a post held or removed after it was shown can still be waved away.
        var posts = db.Posts.Where(p => p.Id == postId);
        var authorId = await (feedback == Feedback.More ? posts.Live(db) : posts).Select(p => (Guid?)p.AuthorId).SingleOrDefaultAsync(cancellationToken);
        if (viewerId is null || authorId is null)
        {
            return false;
        }

        var tags = await db.PostTags.Where(t => t.PostId == postId).Select(t => new { t.Tag, t.Confidence }).ToListAsync(cancellationToken);
        var database = redis.GetDatabase();
        var feedbackKey = FeedbackKey(viewerId.Value);
        var field = postId.ToString();
        var now = (int)feedback;
        for (var attempt = 1; ; attempt++)
        {
            var stored = await database.HashGetAsync(feedbackKey, field);
            var before = stored.HasValue ? (int)stored : 0;
            if (before == now)
            {
                break;
            }
            // Only the same viewer pressing both buttons on one post in a tight loop gets here; better an error than
            // reporting a change that wasn't made
            if (attempt > MaxFeedbackAttempts)
            {
                throw new InvalidOperationException($"Feedback on post {postId} kept changing while it was being saved.");
            }

            // Only applies if nobody changed this post's feedback since it was read; otherwise read it again
            var change = now - before;
            var transaction = database.CreateTransaction();
            transaction.AddCondition(stored.HasValue ? Condition.HashEqual(feedbackKey, field, stored) : Condition.HashNotExists(feedbackKey, field));
            _ = transaction.HashSetAsync(feedbackKey, field, now);
            _ = transaction.KeyExpireAsync(feedbackKey, HiddenLifetime);
            _ = feedback == Feedback.Less
                ? transaction.SetAddAsync(HiddenKey(viewerId.Value), field)
                : transaction.SetRemoveAsync(HiddenKey(viewerId.Value), field);
            _ = transaction.KeyExpireAsync(HiddenKey(viewerId.Value), HiddenLifetime);
            _ = transaction.SortedSetIncrementAsync(EngagementKeys.Affinity(viewerId.Value), authorId.Value.ToString(), FeedbackAuthorWeight * change);
            foreach (var tag in tags)
            {
                _ = transaction.SortedSetIncrementAsync(EngagementKeys.Topics(viewerId.Value), tag.Tag, FeedbackTopicWeight * change * tag.Confidence);
            }
            if (await transaction.ExecuteAsync())
            {
                break;
            }
        }
        await feedLog.MarkAsync(viewerId.Value, postId, feedback == Feedback.Less ? FeedSignal.NotInterested : FeedSignal.ShowMore, cancellationToken);
        return true;
    }

    private async Task<Session> RankAsync(Guid viewerId, ResolvedProfile profile, CancellationToken cancellationToken)
    {
        var settings = profile.Settings;
        var favourites = await FavouriteService.IdsAsync(db, viewerId, cancellationToken);
        var candidates = await CandidatesAsync(viewerId, settings, favourites, cancellationToken);
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
            foreach (var boost in settings.Boosts.Where(b => ProfileRules.Matches(b, c, row, favourites)))
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

    private async Task<List<HomeFeedPostResponse>> CandidatesAsync(Guid viewerId, RankingProfile profile, IReadOnlySet<Guid> favourites,
        CancellationToken cancellationToken)
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

        // Favourites' recent posts, whatever the profile's sources: whether or not the viewer follows them, and whether
        // or not the timeline still holds them. Only live originals, so reposts and removed posts don't use up the limit.
        if (favourites.Count > 0)
        {
            var favouriteIds = favourites.ToList();
            var favouritesSince = now - TimeSpan.FromHours(Math.Max(sources.LookbackHours, FavouritesLookbackHours));
            ids.UnionWith(await db.Posts.Where(p => favouriteIds.Contains(p.AuthorId) && p.CreatedAt >= favouritesSince
                    && p.DeletedAt == null && p.Status == ContentStatus.Visible && p.RepostOfId == null)
                .OrderByDescending(p => p.Id).Select(p => p.Id).Take(FavouritesLimit).ToListAsync(cancellationToken));
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
        return await db.Posts.Where(p => ids.Contains(p.Id) && p.DeletedAt == null && p.Status == ContentStatus.Visible && p.RepostOfId == null
                && p.AuthorId != viewerId && p.CreatedAt >= oldest && db.Users.Any(u => u.Id == p.AuthorId && u.Status == UserStatus.Active))
            .ToFeedPosts(db).ToListAsync(cancellationToken);
    }

    private async Task<Session?> LoadSessionAsync(string key)
    {
        var json = await redis.GetDatabase().StringGetAsync(key);
        return json.HasValue ? JsonSerializer.Deserialize<Session>(json.ToString()) : null;
    }

    // Per profile version, so switching profiles (or saving a new version) never pages through another one's order
    private static string SessionKey(Guid viewerId, ResolvedProfile profile) => $"foryou:{viewerId}:{profile.Label}";

    private static string PeekKey(Guid viewerId, ResolvedProfile profile) => $"foryou-peek:{viewerId}:{profile.Label}";

    private static string HiddenKey(Guid viewerId) => $"hidden:{viewerId}";

    // Favourites' posts from at least the last two days, up to this many, whatever the profile's sources
    private const double FavouritesLookbackHours = 48;
    private const int FavouritesLimit = 100;

    // Post ID to the viewer's feedback on it (Feedback), kept as long as the hidden set
    private static string FeedbackKey(Guid viewerId) => $"feedback:{viewerId}";

    // What one press is worth to the author's affinity and, times the tag's confidence, to each of the post's topics
    private const double FeedbackAuthorWeight = 3;
    private const double FeedbackTopicWeight = 2;

    // Tries at saving feedback while concurrent presses on the same post keep changing it
    private const int MaxFeedbackAttempts = 10;

    private enum Feedback
    {
        Less = -1,
        More = 1,
    }

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
