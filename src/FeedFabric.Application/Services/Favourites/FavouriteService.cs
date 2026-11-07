using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Feed;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Favourites;

// Favourites: a short list of accounts whose posts someone never wants to miss. Kept short on purpose, as Facebook
// does, so a favourite still means something: For You lifts them above everyone else.
public class FavouriteService(AppDbContext db, IMediaService media, FeatureBuilder features, FeedLog feedLog, TimeProvider timeProvider)
    : IFavouriteService
{
    public const int MaxFavourites = 30;

    // Most recently added first
    public async Task<IReadOnlyList<FavouriteResponse>?> ListAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return null;
        }
        return await db.Favourites.AsNoTracking().Where(f => f.UserId == userId)
            .Join(db.Users, f => f.FavouriteId, u => u.Id, (f, u) => new FavouriteResponse
            {
                ExternalUserId = u.ExternalUserId, Username = u.Username, DisplayName = u.DisplayName, AvatarUrl = u.AvatarUrl, AddedAt = f.CreatedAt,
            })
            .OrderByDescending(f => f.AddedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<FavouriteResultStatus> AddAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken)
    {
        var (status, userId, favouriteId) = await FindUsersAsync(externalUserId, targetExternalUserId, cancellationToken);
        if (status != FavouriteResultStatus.Ok)
        {
            return status;
        }

        // Adds for one user take turns on their user row, so two at once can't both count 29 and go in. Only active
        // accounts count towards the limit, so a favourite who's since been suspended doesn't use up a place. ON
        // CONFLICT makes adding one that's already there a no-op, limit or not.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "Users" WHERE "Id" = {userId} FOR UPDATE""", cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var added = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Favourites" ("UserId", "FavouriteId", "CreatedAt")
            SELECT {userId}, {favouriteId}, {now}
            WHERE (SELECT count(*) FROM "Favourites" f JOIN "Users" u ON u."Id" = f."FavouriteId"
                   WHERE f."UserId" = {userId} AND u."Status" = {(int)UserStatus.Active}) < {MaxFavourites}
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        var present = added > 0 || await db.Favourites.AnyAsync(f => f.UserId == userId && f.FavouriteId == favouriteId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return present ? FavouriteResultStatus.Ok : FavouriteResultStatus.LimitReached;
    }

    public async Task<FavouriteResultStatus> RemoveAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken)
    {
        var (status, userId, favouriteId) = await FindUsersAsync(externalUserId, targetExternalUserId, cancellationToken);
        if (status != FavouriteResultStatus.Ok)
        {
            return status;
        }
        await db.Favourites.Where(f => f.UserId == userId && f.FavouriteId == favouriteId).ExecuteDeleteAsync(cancellationToken);
        return FavouriteResultStatus.Ok;
    }

    // Newest first from the viewer's favourites, reposts included, straight from Postgres: at most 30 authors, served
    // by the (AuthorId, Id) index. Logged like the other feeds, so it's training data too.
    public async Task<HomeFeedResponse?> FeedAsync(string externalUserId, HomeFeedQuery query, CancellationToken cancellationToken)
    {
        var userId = await db.Users.FindIdByExternalIdAsync(externalUserId, cancellationToken);
        if (userId is null)
        {
            return null;
        }

        var posts = db.Posts.Where(p => db.Favourites.Any(f => f.UserId == userId && f.FavouriteId == p.AuthorId)).Live(db, userId);
        if (query.Cursor is { } cursor)
        {
            posts = posts.Where(p => p.Id < cursor);
        }
        // One extra post tells us whether there is another page
        var found = await posts.OrderByDescending(p => p.Id).ToFeedPosts(db).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var page = found.Take(query.Limit).ToList();
        await page.AttachRepostsAsync(db, cancellationToken);
        await page.AttachMediaAsync(media, cancellationToken);

        var shown = page.Select(p => p.RepostOf ?? p).ToList();
        var signals = await features.BuildAsync(userId.Value, shown, cancellationToken);
        await feedLog.LogServedAsync(userId.Value, FeedNames.Favourites, "chronological",
            shown.Select((p, i) => new ServedPost(p.Id, i, 0, signals[p.Id])).ToList(), cancellationToken);
        return new HomeFeedResponse { Posts = page, NextCursor = found.Count > query.Limit ? page[^1].Id : null, Source = "postgres" };
    }

    // The viewer's favourites' user IDs, for For You
    public static Task<HashSet<Guid>> IdsAsync(AppDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Favourites.Where(f => f.UserId == userId).Select(f => f.FavouriteId).ToHashSetAsync(cancellationToken);

    private async Task<(FavouriteResultStatus Status, Guid UserId, Guid FavouriteId)> FindUsersAsync(string externalUserId, string targetExternalUserId,
        CancellationToken cancellationToken)
    {
        var (userId, favouriteId) = await db.Users.FindPairAsync(externalUserId, targetExternalUserId, cancellationToken);
        if (userId is null)
        {
            return (FavouriteResultStatus.UserNotFound, Guid.Empty, Guid.Empty);
        }
        if (favouriteId is null)
        {
            return (FavouriteResultStatus.TargetNotFound, Guid.Empty, Guid.Empty);
        }
        return userId == favouriteId
            ? (FavouriteResultStatus.CannotFavouriteSelf, Guid.Empty, Guid.Empty)
            : (FavouriteResultStatus.Ok, userId.Value, favouriteId.Value);
    }
}
