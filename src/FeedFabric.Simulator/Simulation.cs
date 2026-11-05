using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Posts;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Simulator;

// Seeds simulated users and a follow graph where a few accounts have most of the followers,
// then on every tick creates posts and has some users read their home feed and like posts, through the same
// services the API uses.
public class Simulation(
    IServiceScopeFactory scopeFactory,
    Backfill backfill,
    IOptions<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<Simulation> logger) : BackgroundService
{
    public const string UserPrefix = "sim-";
    private const int InsertBatchSize = 5000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var random = new Random(settings.Seed);
        var authors = await SeedAsync(settings, random, stoppingToken);
        await backfill.RunAsync(authors.Count, random, stoppingToken);

        using var timer = new PeriodicTimer(settings.TickInterval, timeProvider);
        var tick = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            tick++;
            using var scope = scopeFactory.CreateScope();
            var postService = scope.ServiceProvider.GetRequiredService<IPostService>();
            for (var i = 0; i < settings.PostsPerTick; i++)
            {
                var number = random.Next(authors.Count) + 1;
                var persona = Persona.ForUser(number);
                // Some posts start a conversation (a question, a milestone, a discussion); the rest are everyday updates
                var content = random.NextDouble() < settings.ConversationShare && Conversations.Library.ByPersona.TryGetValue(persona.Key, out var threads)
                    ? threads[random.Next(threads.Length)].Post
                    : PostContent.Next(persona, random);
                if (random.NextDouble() < settings.PostMentionChance)
                {
                    content += $" with @{SamePersonaUsername(number, authors.Count, random)}";
                }
                await postService.CreateAsync(authors[number - 1], new PostRequest { Content = content }, stoppingToken);
            }
            var (likes, comments) = await EngageAsync(scope.ServiceProvider, settings, authors.Count, random, stoppingToken);
            logger.LogInformation("Tick {Tick}: created {Posts} posts, {Readers} users read their feed, liked {Likes} posts and left {Comments} comments",
                tick, settings.PostsPerTick, settings.ReadersPerTick, likes, comments);
        }
    }

    // For You as this reader's profile has it; a profile that's been deleted falls back to the default
    private static async Task<ForYouResponse?> ReadForYouAsync(ForYouService forYou, RankingProfileStore profiles, string reader, int number,
        SimulatorOptions settings, CancellationToken cancellationToken)
    {
        var key = settings.ForYouProfiles.Count == 0 ? null : settings.ForYouProfiles[number / 2 % settings.ForYouProfiles.Count];
        var profile = await profiles.GetAsync(key ?? profiles.DefaultKey, cancellationToken) ?? await profiles.ResolveAsync(null, cancellationToken);
        return await forYou.GetAsync(reader, new ForYouQuery { Limit = settings.PostsReadPerVisit }, profile!, cancellationToken);
    }

    // Readers act on what they're shown the way people roughly do: mostly on what interests them (Interests), more
    // near the top of the feed than further down, with good and bad moments, the odd thing just because it's new, and
    // now and then "Not interested" on something off-topic. Persona still drives most of it, so the dashboard can tell
    // whether a ranker finds people's interests, but it's no longer the whole story.
    private async Task<(int Likes, int Comments)> EngageAsync(IServiceProvider services, SimulatorOptions settings, int userCount, Random random, CancellationToken cancellationToken)
    {
        var homeFeed = services.GetRequiredService<IHomeFeedService>();
        var forYou = services.GetRequiredService<ForYouService>();
        var profiles = services.GetRequiredService<RankingProfileStore>();
        var likeService = services.GetRequiredService<ILikeService>();
        var commentService = services.GetRequiredService<ICommentService>();
        var impressions = services.GetRequiredService<IImpressionService>();
        var likes = 0;
        var comments = 0;
        for (var i = 0; i < settings.ReadersPerTick; i++)
        {
            var number = random.Next(userCount) + 1;
            var reader = $"{UserPrefix}{number}";
            var interests = Interests.For(number, settings);
            // The experiment: even-numbered users read For You (ranked), odd-numbered ones Following (newest first),
            // so the dashboard can compare the two on the same population
            var posts = number % 2 == 0
                ? (await ReadForYouAsync(forYou, profiles, reader, number, settings, cancellationToken))?.Posts ?? []
                : (await homeFeed.GetHomeFeedAsync(reader, new HomeFeedQuery { Limit = settings.PostsReadPerVisit }, cancellationToken))?.Posts ?? [];

            // How drawn in they are by each post, where it sits in the feed
            var pull = posts.Select((p, position) =>
            {
                var mood = 0.5 + random.NextDouble();
                var novelty = interests.Novelty * random.NextDouble();
                return Math.Min(1, (interests.In(p.AuthorExternalUserId) * mood + novelty) / (1 + settings.PositionDecay * position));
            }).ToList();

            // Everything scrolled past was seen; they linger on what pulls them in and flick past the rest
            await impressions.RecordAsync(reader, posts.Select(p => p.Id).ToList(), cancellationToken);
            await impressions.RecordDwellAsync(reader, posts.Select((p, k) => (p.Id, DwellMs(pull[k], random))).ToList(), cancellationToken);

            for (var k = 0; k < posts.Count; k++)
            {
                var post = posts[k];
                if (post.AuthorExternalUserId == reader || !post.AuthorExternalUserId.StartsWith(UserPrefix))
                {
                    continue;
                }

                if (interests.IsOffTopic(post.AuthorExternalUserId) && random.NextDouble() < settings.NotInterestedChance)
                {
                    await forYou.NotInterestedAsync(reader, post.Id, cancellationToken);
                    continue;
                }

                if (random.NextDouble() < settings.OpenChance * pull[k])
                {
                    await impressions.RecordOpenAsync(reader, post.Id, cancellationToken);
                }

                if (random.NextDouble() < settings.LikeChance * pull[k])
                {
                    await likeService.LikeAsync(reader, post.Id, cancellationToken);
                    likes++;

                    if (random.NextDouble() < settings.CommentChance)
                    {
                        await CommentAsync(commentService, reader, post, settings, random, cancellationToken);
                        comments++;
                    }
                }
            }
        }
        return (likes, comments);
    }

    // Time on screen: a long read for what pulls them in, a glance or a flick past for the rest
    private static int DwellMs(double pull, Random random) =>
        pull > 0.5 ? random.Next(4000, 20000)
        : pull > 0.2 ? random.Next(1200, 6000)
        : random.NextDouble() < 0.6 ? random.Next(250, 900) : random.Next(900, 2500);

    // Either a new comment on the post, or a reply to one of the comments already on it. The words fit the post:
    // advice in its topic for a question, congratulations for a win, agreement when joining in under someone else.
    private static async Task CommentAsync(ICommentService commentService, string reader, HomeFeedPostResponse post, SimulatorOptions settings,
        Random random, CancellationToken cancellationToken)
    {
        Guid? parentId = null;
        var answering = post.AuthorUsername;
        if (random.NextDouble() < settings.ReplyChance)
        {
            var existing = await commentService.ListAsync(reader, post.Id, new CommentQuery { Limit = 10 }, cancellationToken);
            var candidates = existing.Page?.Comments.Where(c => !c.IsDeleted && c.AuthorExternalUserId != reader).ToList() ?? [];
            if (candidates.Count > 0)
            {
                var parent = candidates[random.Next(candidates.Count)];
                parentId = parent.Id;
                answering = parent.AuthorUsername;
            }
        }

        var authorPersona = post.AuthorExternalUserId.StartsWith(UserPrefix) && int.TryParse(post.AuthorExternalUserId[UserPrefix.Length..], out var n)
            ? Persona.ForUser(n).Key
            : null;
        var content = Conversations.Library.CommentFor(post.Content, authorPersona, replyFromPoster: false, isReply: parentId is not null, random);
        if (random.NextDouble() < settings.CommentMentionChance)
        {
            content = $"@{answering} {content}";
        }
        await commentService.CreateAsync(reader, post.Id, new CommentRequest { Content = content, ParentCommentId = parentId }, cancellationToken);
    }

    // Another simulated user with the same persona: users with the same persona are 12 numbers apart
    private static string SamePersonaUsername(int number, int userCount, Random random)
    {
        var count = Math.Max(1, (userCount - 1) / Persona.All.Length);
        var other = (number - 1) % Persona.All.Length + 1 + Persona.All.Length * random.Next(count);
        if (other == number || other > userCount)
        {
            other = (number - 1) % Persona.All.Length + 1;
        }
        return Persona.ForUser(other).Username(other);
    }

    // Returns the simulated users' external IDs. Safe to run again: existing users and follows are kept.
    private async Task<List<string>> SeedAsync(SimulatorOptions settings, Random random, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var externalIds = Enumerable.Range(1, settings.Users).Select(n => $"{UserPrefix}{n}").ToList();
        var existing = await db.Users
            .Where(u => externalIds.Contains(u.ExternalUserId))
            .Select(u => u.ExternalUserId)
            .ToListAsync(cancellationToken);
        var missing = externalIds.Except(existing).ToList();
        foreach (var chunk in missing.Chunk(InsertBatchSize))
        {
            db.Users.AddRange(chunk.Select(id => new UserEntity { ExternalUserId = id, Username = PersonaUsername(id), SyncedAt = now }));
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("Simulated users: {Created} created, {Existing} already there", missing.Count, existing.Count);

        // Users created before personas existed still have their external ID as username
        var unnamed = await db.Users
            .Where(u => externalIds.Contains(u.ExternalUserId) && u.Username == u.ExternalUserId)
            .ToListAsync(cancellationToken);
        foreach (var user in unnamed)
        {
            user.Username = PersonaUsername(user.ExternalUserId);
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        // Ordered by popularity rank: index 0 is sim-1
        var idsByExternalId = await db.Users
            .Where(u => externalIds.Contains(u.ExternalUserId))
            .ToDictionaryAsync(u => u.ExternalUserId, u => u.Id, cancellationToken);
        var ranked = externalIds.Select(id => idsByExternalId[id]).ToList();

        if (await db.Follows.AnyAsync(f => f.FolloweeId == ranked[0], cancellationToken))
        {
            logger.LogInformation("Follow graph already seeded");
            return externalIds;
        }

        // Every user follows simulated accounts, including the dev users, so X-Dev-User 1 gets a timeline too
        var followers = await db.Users.Select(u => u.Id).ToListAsync(cancellationToken);
        var cumulative = CumulativeZipfWeights(ranked.Count, settings.PopularityExponent);
        var total = 0;
        var pending = new List<FollowEntity>(InsertBatchSize);
        foreach (var followerId in followers)
        {
            var count = Math.Min(random.Next(settings.MinFollowing, settings.MaxFollowing + 1), ranked.Count - 1);
            var followees = new HashSet<Guid>();
            // Popular accounts come up again and again, so cap the draws rather than loop until the count is hit
            for (var draws = 0; followees.Count < count && draws < count * 4; draws++)
            {
                var followeeId = ranked[SampleIndex(cumulative, random)];
                if (followeeId != followerId)
                {
                    followees.Add(followeeId);
                }
            }

            pending.AddRange(followees.Select(followeeId => new FollowEntity { FollowerId = followerId, FolloweeId = followeeId, CreatedAt = now }));
            if (pending.Count >= InsertBatchSize)
            {
                total += await InsertAsync(db, pending, cancellationToken);
            }
        }
        total += await InsertAsync(db, pending, cancellationToken);

        var topFollowers = await db.Follows.CountAsync(f => f.FolloweeId == ranked[0], cancellationToken);
        logger.LogInformation("Follow graph seeded: {Follows} follows; {Top} has {TopFollowers} followers", total, externalIds[0], topFollowers);
        return externalIds;
    }

    private static string PersonaUsername(string externalId)
    {
        var number = int.Parse(externalId[UserPrefix.Length..]);
        return Persona.ForUser(number).Username(number);
    }

    private static async Task<int> InsertAsync(AppDbContext db, List<FollowEntity> follows, CancellationToken cancellationToken)
    {
        db.Follows.AddRange(follows);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        var count = follows.Count;
        follows.Clear();
        return count;
    }

    // Rank r gets weight 1 / r^exponent, so rank 1 is followed far more than rank 1000
    private static double[] CumulativeZipfWeights(int count, double exponent)
    {
        var cumulative = new double[count];
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            sum += 1.0 / Math.Pow(i + 1, exponent);
            cumulative[i] = sum;
        }
        return cumulative;
    }

    private static int SampleIndex(double[] cumulative, Random random)
    {
        var index = Array.BinarySearch(cumulative, random.NextDouble() * cumulative[^1]);
        return index >= 0 ? index : ~index;
    }
}
