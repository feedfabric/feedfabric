using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Application.Services.Follows;

public class FollowService(AppDbContext db, TimeProvider timeProvider) : IFollowService
{
    public async Task<FollowResultStatus> FollowAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken)
    {
        var (status, followerId, followeeId) = await FindUsersAsync(externalUserId, targetExternalUserId, cancellationToken);
        if (status != FollowResultStatus.Ok)
        {
            return status;
        }

        // ON CONFLICT keeps two simultaneous follows from failing on the primary key
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Follows" ("FollowerId", "FolloweeId", "CreatedAt") VALUES ({followerId}, {followeeId}, {now}) ON CONFLICT DO NOTHING""",
            cancellationToken);

        return FollowResultStatus.Ok;
    }

    public async Task<FollowResultStatus> UnfollowAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken)
    {
        var (status, followerId, followeeId) = await FindUsersAsync(externalUserId, targetExternalUserId, cancellationToken);
        if (status != FollowResultStatus.Ok)
        {
            return status;
        }

        await db.Follows
            .Where(f => f.FollowerId == followerId && f.FolloweeId == followeeId)
            .ExecuteDeleteAsync(cancellationToken);

        return FollowResultStatus.Ok;
    }

    private async Task<(FollowResultStatus Status, Guid FollowerId, Guid FolloweeId)> FindUsersAsync(string externalUserId, string targetExternalUserId, CancellationToken cancellationToken)
    {
        var (followerId, followeeId) = await db.Users.FindPairAsync(externalUserId, targetExternalUserId, cancellationToken);
        if (followerId is null)
        {
            return (FollowResultStatus.UserNotFound, default, default);
        }
        if (followeeId is null)
        {
            return (FollowResultStatus.TargetNotFound, default, default);
        }

        return followerId == followeeId
            ? (FollowResultStatus.CannotFollowSelf, default, default)
            : (FollowResultStatus.Ok, followerId.Value, followeeId.Value);
    }
}
