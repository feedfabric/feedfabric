using Microsoft.EntityFrameworkCore;
using FeedFabric.Api.Auth;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Impressions;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Api.Admin;

// GET /admin/dashboard?minutes=60: everything the admin panel's overview shows, in one call. Staff only.
public static class DashboardEndpoints
{
    private const int TopCount = 10;

    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/dashboard", async (int? minutes, AppDbContext db, IImpressionService impressions, ITagService tags, ReviewService reviews,
            RankerModel model, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            var window = Math.Clamp(minutes ?? 60, 5, 24 * 60);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var since = now.AddMinutes(-window);

            // Activity in the window
            var posts = await db.Posts.CountAsync(p => p.CreatedAt >= since && p.DeletedAt == null && p.RepostOfId == null, cancellationToken);
            var likes = await db.Likes.CountAsync(l => l.CreatedAt >= since, cancellationToken);
            var comments = await db.Comments.CountAsync(c => c.CreatedAt >= since && c.DeletedAt == null, cancellationToken);
            var actors = (await db.Posts.Where(p => p.CreatedAt >= since).Select(p => p.AuthorId).Distinct().ToListAsync(cancellationToken))
                .Concat(await db.Likes.Where(l => l.CreatedAt >= since).Select(l => l.UserId).Distinct().ToListAsync(cancellationToken))
                .Concat(await db.Comments.Where(c => c.CreatedAt >= since).Select(c => c.AuthorId).Distinct().ToListAsync(cancellationToken))
                .Distinct().Count();

            // Per minute: posts, likes and comments from Postgres, impressions from Redis
            var signalSeries = await impressions.PerMinuteAsync(Math.Min(window, 360));
            var seriesSince = signalSeries[0].Minute;
            var postsPerMinute = await PerMinuteAsync(db, "Posts", seriesSince, cancellationToken);
            var likesPerMinute = await PerMinuteAsync(db, "Likes", seriesSince, cancellationToken);
            var commentsPerMinute = await PerMinuteAsync(db, "Comments", seriesSince, cancellationToken);
            var series = signalSeries.Select(m => new
            {
                m.Minute,
                Posts = postsPerMinute.GetValueOrDefault(m.Minute),
                Likes = likesPerMinute.GetValueOrDefault(m.Minute),
                Comments = commentsPerMinute.GetValueOrDefault(m.Minute),
                m.Impressions,
                m.Opens,
                AverageDwellSeconds = m.DwellViews == 0 ? (double?)null : Math.Round(m.DwellMs / 1000.0 / m.DwellViews, 1),
            }).ToList();
            var inWindow = signalSeries.Where(m => m.Minute >= since).ToList();
            var impressionsInWindow = inWindow.Sum(m => m.Impressions);
            var opensInWindow = inWindow.Sum(m => m.Opens);
            var dwellViews = inWindow.Sum(m => m.DwellViews);
            var dwellMs = inWindow.Sum(m => m.DwellMs);
            var dwellDistribution = DwellBuckets.All.Select(b => new { b.Label, Views = inWindow.Sum(m => m.DwellBuckets[b.Field]) }).ToList();

            // Most seen posts of all time, with how much they were engaged with
            var top = await impressions.TopPostsAsync(TopCount);
            var topIds = top.Select(t => t.PostId).ToList();
            var counts = await impressions.SignalsAsync(topIds);
            var topPosts = await PostRowsAsync(db, topIds, cancellationToken);
            var mostSeen = top.Where(t => topPosts.ContainsKey(t.PostId)).Select(t =>
            {
                var p = topPosts[t.PostId];
                var signals = counts.GetValueOrDefault(t.PostId);
                return new
                {
                    p.Id, p.Content, p.AuthorUsername, p.AuthorExternalUserId, p.Likes, p.Comments,
                    Impressions = t.Impressions,
                    Viewers = signals?.Viewers ?? 0,
                    Opens = signals?.Opens ?? 0,
                    AverageDwellSeconds = signals?.AverageDwellSeconds,
                    ClickThroughRate = t.Impressions == 0 ? 0 : Math.Round((signals?.Opens ?? 0) * 100.0 / t.Impressions, 1),
                    EngagementRate = t.Impressions == 0 ? 0 : Math.Round((p.Likes + p.Comments) * 100.0 / t.Impressions, 1),
                };
            }).ToList();

            // Most liked in the window
            var likedIds = await db.Likes.Where(l => l.CreatedAt >= since)
                .GroupBy(l => l.PostId).OrderByDescending(g => g.Count()).Take(TopCount).Select(g => g.Key)
                .ToListAsync(cancellationToken);
            var likedRows = await PostRowsAsync(db, likedIds, cancellationToken);
            var likedImpressions = await impressions.SignalsAsync(likedIds);
            var mostLiked = likedIds.Where(likedRows.ContainsKey).Select(id => new
            {
                likedRows[id].Id, likedRows[id].Content, likedRows[id].AuthorUsername, likedRows[id].AuthorExternalUserId,
                likedRows[id].Likes, likedRows[id].Comments, Impressions = likedImpressions.GetValueOrDefault(id)?.Impressions ?? 0,
            }).ToList();

            // Authors whose posts got the most likes and comments in the window
            var likesByAuthor = await db.Likes.Where(l => l.CreatedAt >= since)
                .Join(db.Posts, l => l.PostId, p => p.Id, (l, p) => p.AuthorId)
                .GroupBy(a => a).Select(g => new { AuthorId = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var commentsByAuthor = await db.Comments.Where(c => c.CreatedAt >= since && c.DeletedAt == null)
                .Join(db.Posts, c => c.PostId, p => p.Id, (c, p) => p.AuthorId)
                .GroupBy(a => a).Select(g => new { AuthorId = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var scores = likesByAuthor.Select(x => (x.AuthorId, Likes: x.Count, Comments: 0))
                .Concat(commentsByAuthor.Select(x => (x.AuthorId, Likes: 0, Comments: x.Count)))
                .GroupBy(x => x.AuthorId)
                .Select(g => (AuthorId: g.Key, Likes: g.Sum(x => x.Likes), Comments: g.Sum(x => x.Comments)))
                .OrderByDescending(x => x.Likes + x.Comments * 2).Take(TopCount).ToList();
            var authorIds = scores.Select(s => s.AuthorId).ToList();
            var names = await db.Users.Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.ExternalUserId, u.Username }, cancellationToken);
            var topAuthors = scores.Where(s => names.ContainsKey(s.AuthorId)).Select(s => new
            {
                names[s.AuthorId].ExternalUserId, names[s.AuthorId].Username, s.Likes, s.Comments,
            }).ToList();

            // How often a like goes to someone with the same persona: the signal a ranking model would learn from
            var samePersona = await db.Database.SqlQuery<double?>($"""
                SELECT avg((split_part(liker."Username", '_', 1) = split_part(author."Username", '_', 1))::int) * 100 AS "Value"
                FROM "Likes" l
                JOIN "Users" liker ON liker."Id" = l."UserId"
                JOIN "Posts" p ON p."Id" = l."PostId"
                JOIN "Users" author ON author."Id" = p."AuthorId"
                WHERE l."CreatedAt" >= {since} AND liker."ExternalUserId" LIKE 'sim-%' AND author."ExternalUserId" LIKE 'sim-%'
                """).SingleAsync(cancellationToken);

            // Photos: how many passed and why the rest didn't
            var media = await db.Media.GroupBy(m => m.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var rejections = await db.Media.Where(m => m.Status == MediaStatus.Rejected)
                .GroupBy(m => m.RejectionReason).Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(r => r.Count).ToListAsync(cancellationToken);

            // Pipeline: events waiting to be published, and when each consumer last finished one
            var outboxWaiting = await db.OutboxMessages.CountAsync(o => o.ProcessedAt == null, cancellationToken);
            var oldestWaiting = await db.OutboxMessages.Where(o => o.ProcessedAt == null).MinAsync(o => (DateTime?)o.CreatedAt, cancellationToken);
            var consumers = await db.ProcessedEvents.GroupBy(e => e.Consumer)
                .Select(g => new { Consumer = g.Key, LastProcessedAt = g.Max(e => e.ProcessedAt) })
                .ToListAsync(cancellationToken);
            var lastMediaProcessed = await db.Media.MaxAsync(m => m.ProcessedAt, cancellationToken);

            // Ranking: For You against Following on the same simulated population (even-numbered users read For You).
            // Rates are per post seen; a post served but scrolled past unseen says nothing either way.
            var feeds = await db.FeedLog.Where(f => f.ServedAt >= since)
                .GroupBy(f => f.Feed)
                .Select(g => new
                {
                    Feed = g.Key,
                    Served = g.Count(),
                    Seen = g.Count(f => f.Seen),
                    Opened = g.Count(f => f.Opened),
                    Liked = g.Count(f => f.Liked),
                    Commented = g.Count(f => f.Commented),
                    Engaged = g.Count(f => f.Opened || f.Liked || f.Commented),
                    DwellViews = g.Count(f => f.DwellMs > 0),
                    DwellMs = g.Sum(f => (long)f.DwellMs),
                })
                .ToListAsync(cancellationToken);
            var personaShare = await db.Database.SqlQueryRaw<FeedShare>("""
                SELECT f."Feed" AS "Feed", avg((split_part(v."Username", '_', 1) = split_part(a."Username", '_', 1))::int) * 100 AS "Share"
                FROM "FeedLog" f
                JOIN "Users" v ON v."Id" = f."ViewerId"
                JOIN "Posts" p ON p."Id" = f."PostId"
                JOIN "Users" a ON a."Id" = p."AuthorId"
                WHERE f."ServedAt" >= @p0 AND f."Seen" AND v."ExternalUserId" LIKE 'sim-%' AND a."ExternalUserId" LIKE 'sim-%'
                GROUP BY f."Feed"
                """, since).ToDictionaryAsync(r => r.Feed, r => r.Share, cancellationToken);
            var personaEngagedShare = await db.Database.SqlQueryRaw<FeedShare>("""
                SELECT f."Feed" AS "Feed", avg((split_part(v."Username", '_', 1) = split_part(a."Username", '_', 1))::int) * 100 AS "Share"
                FROM "FeedLog" f
                JOIN "Users" v ON v."Id" = f."ViewerId"
                JOIN "Posts" p ON p."Id" = f."PostId"
                JOIN "Users" a ON a."Id" = p."AuthorId"
                WHERE f."ServedAt" >= @p0 AND (f."Opened" OR f."Liked" OR f."Commented")
                  AND v."ExternalUserId" LIKE 'sim-%' AND a."ExternalUserId" LIKE 'sim-%'
                GROUP BY f."Feed"
                """, since).ToDictionaryAsync(r => r.Feed, r => r.Share, cancellationToken);
            var rate = (int count, int seen) => seen == 0 ? 0 : Math.Round(count * 100.0 / seen, 2);
            var experiment = feeds.OrderBy(f => f.Feed == FeedNames.ForYou ? 0 : 1).Select(f => new
            {
                f.Feed,
                f.Served,
                f.Seen,
                OpenRate = rate(f.Opened, f.Seen),
                LikeRate = rate(f.Liked, f.Seen),
                CommentRate = rate(f.Commented, f.Seen),
                EngagementRate = rate(f.Engaged, f.Seen),
                AverageDwellSeconds = f.DwellViews == 0 ? (double?)null : Math.Round(f.DwellMs / 1000.0 / f.DwellViews, 1),
                // Of the posts it showed, how many were from someone with the viewer's persona: did it find their interests?
                SamePersonaShownShare = personaShare.TryGetValue(f.Feed, out var shown) ? Math.Round(shown, 1) : (double?)null,
                SamePersonaEngagedShare = personaEngagedShare.TryGetValue(f.Feed, out var engaged) ? Math.Round(engaged, 1) : (double?)null,
            }).ToList();

            // Exploration slots against ranked ones: how well do posts the ranker didn't choose do?
            var exploration = await db.FeedLog.Where(f => f.ServedAt >= since && f.Feed == FeedNames.ForYou && f.Seen)
                .GroupBy(f => f.Explored)
                .Select(g => new { Explored = g.Key, Seen = g.Count(), Engaged = g.Count(f => f.Opened || f.Liked || f.Commented) })
                .ToListAsync(cancellationToken);
            // Both in one pass over the window
            var feedback = await db.FeedLog.Where(f => f.ServedAt >= since && (f.NotInterested || f.ShowMore))
                .GroupBy(_ => 1)
                .Select(g => new { NotInterested = g.Count(f => f.NotInterested), ShowMore = g.Count(f => f.ShowMore) })
                .SingleOrDefaultAsync(cancellationToken);

            // For You by ranking profile: which settings people respond to best (the simulator splits readers between
            // Simulator:ForYouProfiles)
            var byProfile = (await db.FeedLog.Where(f => f.ServedAt >= since && f.Feed == FeedNames.ForYou && f.Profile != null)
                .GroupBy(f => f.Profile!)
                .Select(g => new
                {
                    Profile = g.Key,
                    Served = g.Count(),
                    Seen = g.Count(f => f.Seen),
                    Opened = g.Count(f => f.Opened),
                    Liked = g.Count(f => f.Liked),
                    Commented = g.Count(f => f.Commented),
                    Engaged = g.Count(f => f.Opened || f.Liked || f.Commented),
                    NotInterested = g.Count(f => f.NotInterested),
                    ShowMore = g.Count(f => f.ShowMore),
                    DwellViews = g.Count(f => f.DwellMs > 0),
                    DwellMs = g.Sum(f => (long)f.DwellMs),
                })
                .ToListAsync(cancellationToken))
                .OrderByDescending(p => p.Served)
                .Select(p => new
                {
                    p.Profile,
                    p.Served,
                    p.Seen,
                    OpenRate = rate(p.Opened, p.Seen),
                    LikeRate = rate(p.Liked, p.Seen),
                    CommentRate = rate(p.Commented, p.Seen),
                    EngagementRate = rate(p.Engaged, p.Seen),
                    NotInterestedRate = rate(p.NotInterested, p.Seen),
                    ShowMoreRate = rate(p.ShowMore, p.Seen),
                    AverageDwellSeconds = p.DwellViews == 0 ? (double?)null : Math.Round(p.DwellMs / 1000.0 / p.DwellViews, 1),
                })
                .ToList();

            await model.EnsureLoadedAsync(cancellationToken);
            var ranking = new
            {
                Exploration = exploration.Select(e => new { e.Explored, e.Seen, EngagementRate = rate(e.Engaged, e.Seen) }),
                NotInterested = feedback?.NotInterested ?? 0,
                ShowMore = feedback?.ShowMore ?? 0,
                Ranker = model.Info is null ? "heuristic" : $"model:{model.Info.Version}",
                Model = model.Info,
                Experiment = experiment,
                Profiles = byProfile,
                TrainingRows = await db.FeedLog.CountAsync(f => f.Seen, cancellationToken),
                TrainingPositives = await db.FeedLog.CountAsync(f => f.Seen && (f.Opened || f.Liked || f.Commented), cancellationToken),
            };

            // Safety: what was blocked, or held for review, before anyone saw it. Reviewing happens in the admin app.
            var blocked = await db.ModerationEvents.Where(m => m.CreatedAt >= since)
                .GroupBy(m => new { m.Kind, m.Category, m.Action })
                .Select(g => new { g.Key.Kind, g.Key.Category, g.Key.Action, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync(cancellationToken);
            var latestBlocks = await db.ModerationEvents.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(8)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Kind, m.Category, m.Action, m.Score, m.Excerpt, m.CreatedAt, u.Username, u.ExternalUserId })
                .ToListAsync(cancellationToken);
            var safety = new
            {
                Blocked = blocked,
                Total = blocked.Sum(b => b.Count),
                Latest = latestBlocks,
                AwaitingReview = (await reviews.OverviewAsync(cancellationToken)).Waiting,
            };

            return Results.Ok(new
            {
                WindowMinutes = window,
                Ranking = ranking,
                Safety = safety,
                GeneratedAt = now,
                Window = new
                {
                    Posts = posts, Likes = likes, Comments = comments, Impressions = impressionsInWindow, ActiveUsers = actors,
                    EngagementRate = impressionsInWindow == 0 ? 0 : Math.Round((likes + comments) * 100.0 / impressionsInWindow, 2),
                    Opens = opensInWindow,
                    ClickThroughRate = impressionsInWindow == 0 ? 0 : Math.Round(opensInWindow * 100.0 / impressionsInWindow, 2),
                    AverageDwellSeconds = dwellViews == 0 ? (double?)null : Math.Round(dwellMs / 1000.0 / dwellViews, 1),
                    SamePersonaLikeShare = samePersona is null ? (double?)null : Math.Round(samePersona.Value, 1),
                },
                Totals = new
                {
                    Users = await db.Users.CountAsync(cancellationToken),
                    Posts = await db.Posts.CountAsync(p => p.DeletedAt == null && p.RepostOfId == null, cancellationToken),
                    Likes = await db.Likes.CountAsync(cancellationToken),
                    Comments = await db.Comments.CountAsync(c => c.DeletedAt == null, cancellationToken),
                    Follows = await db.Follows.CountAsync(cancellationToken),
                    Mentions = await db.Mentions.CountAsync(cancellationToken),
                },
                Series = series,
                DwellDistribution = dwellDistribution,
                MostSeen = mostSeen,
                MostLiked = mostLiked,
                TopAuthors = topAuthors,
                Trending = await tags.TrendingAsync(Math.Max(1, (int)Math.Ceiling(window / 60.0)), TopCount, cancellationToken),
                Media = new
                {
                    ByStatus = media.Select(m => new { Status = m.Status.ToString(), m.Count }),
                    Rejections = rejections,
                    LastProcessedAt = lastMediaProcessed,
                },
                Pipeline = new
                {
                    OutboxWaiting = outboxWaiting,
                    OldestWaitingSeconds = oldestWaiting is null ? (double?)null : Math.Round((now - oldestWaiting.Value).TotalSeconds),
                    Consumers = consumers,
                },
            });
        }).RequireAuthorization(AdminPolicies.Staff);
    }

    private sealed class FeedShare
    {
        public required string Feed { get; set; }

        public double Share { get; set; }
    }

    private sealed class MinuteCount
    {
        public DateTime Minute { get; set; }

        public int Count { get; set; }
    }

    // Rows per minute since `since` from one table's CreatedAt; the table name is one of ours, never user input
    private static async Task<Dictionary<DateTime, int>> PerMinuteAsync(AppDbContext db, string table, DateTime since, CancellationToken cancellationToken)
    {
        var sql = $"""SELECT date_trunc('minute', "CreatedAt") AS "Minute", count(*)::int AS "Count" FROM "{table}" WHERE "CreatedAt" >= @p0 GROUP BY 1""";
        var rows = await db.Database.SqlQueryRaw<MinuteCount>(sql, since).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => DateTime.SpecifyKind(r.Minute, DateTimeKind.Utc), r => r.Count);
    }

    private sealed record PostRow(Guid Id, string Content, string AuthorUsername, string AuthorExternalUserId, int Likes, int Comments);

    private static async Task<Dictionary<Guid, PostRow>> PostRowsAsync(AppDbContext db, List<Guid> ids, CancellationToken cancellationToken) =>
        await db.Posts.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.DeletedAt == null)
            .Join(db.Users, p => p.AuthorId, u => u.Id, (p, u) => new PostRow(
                p.Id, p.Content, u.Username, u.ExternalUserId,
                db.Likes.Count(l => l.PostId == p.Id),
                db.Comments.Count(c => c.PostId == p.Id && c.DeletedAt == null)))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
}
