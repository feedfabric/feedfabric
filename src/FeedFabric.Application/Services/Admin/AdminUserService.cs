using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Admin;

public enum AdminUserSort
{
    Username,
    Posts,
    Reports,
    Flags,
}

// Users as staff see them: search, a profile with everything moderation knows, and suspending or verifying
public class AdminUserService(AppDbContext db, ReviewService reviews, AdminPostService posts, TimeProvider timeProvider)
{
    private const int RecentCount = 20;

    public async Task<AdminPage<AdminUserRow>> SearchAsync(string? q, UserStatus? status, AdminUserSort sort, int offset, int limit,
        CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            users = users.Where(u => u.Username.Contains(term) || u.ExternalUserId == q.Trim()
                || (u.DisplayName != null && u.DisplayName.ToLower().Contains(term)));
        }
        if (status is { } s)
        {
            users = users.Where(u => u.Status == s);
        }

        var rows = Rows(users);
        rows = sort switch
        {
            AdminUserSort.Posts => rows.OrderByDescending(r => r.Posts).ThenBy(r => r.Username),
            AdminUserSort.Reports => rows.OrderByDescending(r => r.ReportsAgainst).ThenBy(r => r.Username),
            AdminUserSort.Flags => rows.OrderByDescending(r => r.Flags).ThenBy(r => r.Username),
            _ => rows.OrderBy(r => r.Username),
        };
        var total = await users.CountAsync(cancellationToken);
        var page = await rows.Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new AdminPage<AdminUserRow>(page.Select(r => r.ToRow()).ToList(), total);
    }

    public async Task<AdminUserDetail?> DetailAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var row = await Rows(db.Users.AsNoTracking().Where(u => u.ExternalUserId == externalUserId)).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var id = row.Id;
        var recentPosts = await posts.RowsAsync(db.Posts.Where(p => p.AuthorId == id && p.RepostOfId == null)
            .OrderByDescending(p => p.Id).Take(RecentCount), cancellationToken);
        var comments = await AdminPostService.CommentRows(db, db.Comments.Where(c => c.AuthorId == id).OrderByDescending(c => c.Id).Take(RecentCount))
            .ToListAsync(cancellationToken);
        var history = await reviews.LogAsync(null, 30, cancellationToken, userId: id);
        var reports = await reviews.ReportsAsync(30, cancellationToken, targetAuthorId: id);
        return new AdminUserDetail(
            row.ToRow(),
            await db.Follows.CountAsync(f => f.FollowerId == id, cancellationToken),
            await db.Comments.CountAsync(c => c.AuthorId == id && c.DeletedAt == null, cancellationToken),
            await db.Likes.CountAsync(l => l.UserId == id, cancellationToken),
            recentPosts, comments, history, reports);
    }

    // Suspend or reinstate, recorded in the moderation log against the user. False when there's no such user.
    public async Task<bool> SetStatusAsync(string externalUserId, UserStatus status, string reviewer, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalUserId == externalUserId, cancellationToken);
        if (user is null)
        {
            return false;
        }
        if (user.Status == status)
        {
            return true;
        }

        user.Status = status;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var suspended = status == UserStatus.Suspended;
        db.ModerationEvents.Add(new ModerationEventEntity
        {
            UserId = user.Id,
            Kind = "user",
            Action = ModerationActions.Reviewed,
            Category = suspended ? ModerationCategories.Suspended : ModerationCategories.Reinstated,
            Excerpt = user.Username,
            CreatedAt = now,
            Decision = suspended ? ReviewDecisions.Suspended : ReviewDecisions.Reinstated,
            ReviewedBy = reviewer,
            ReviewedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetVerifiedAsync(string externalUserId, bool verified, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.ExternalUserId == externalUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsVerified, verified), cancellationToken) > 0;

    private IQueryable<UserRowData> Rows(IQueryable<UserEntity> users) => users.Select(u => new UserRowData
    {
        Id = u.Id,
        ExternalUserId = u.ExternalUserId,
        Username = u.Username,
        DisplayName = u.DisplayName,
        AvatarUrl = u.AvatarUrl,
        Status = u.Status,
        IsVerified = u.IsVerified,
        SyncedAt = u.SyncedAt,
        Posts = db.Posts.Count(p => p.AuthorId == u.Id && p.DeletedAt == null && p.RepostOfId == null),
        Followers = db.Follows.Count(f => f.FolloweeId == u.Id),
        ReportsAgainst = db.Reports.Count(r => r.CommentId == null
            ? db.Posts.Any(p => p.Id == r.PostId && p.AuthorId == u.Id)
            : db.Comments.Any(c => c.Id == r.CommentId && c.AuthorId == u.Id)),
        Flags = db.ModerationEvents.Count(m => m.UserId == u.Id && m.Kind != "user"),
    });

    private sealed class UserRowData
    {
        public Guid Id { get; init; }
        public required string ExternalUserId { get; init; }
        public required string Username { get; init; }
        public string? DisplayName { get; init; }
        public string? AvatarUrl { get; init; }
        public UserStatus Status { get; init; }
        public bool IsVerified { get; init; }
        public DateTime SyncedAt { get; init; }
        public int Posts { get; init; }
        public int Followers { get; init; }
        public int ReportsAgainst { get; init; }
        public int Flags { get; init; }

        public AdminUserRow ToRow() => new(ExternalUserId, Username, DisplayName, AvatarUrl, Status.ToString().ToLowerInvariant(), IsVerified,
            SyncedAt, Posts, Followers, ReportsAgainst, Flags);
    }
}
