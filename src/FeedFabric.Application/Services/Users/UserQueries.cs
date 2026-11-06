using Microsoft.EntityFrameworkCore;

namespace FeedFabric.Application.Services.Users;

public static class UserQueries
{
    // Maps the signed-in user's external ID to the internal UserEntity.Id, or null if they haven't been synced
    public static Task<Guid?> FindIdByExternalIdAsync(this IQueryable<UserEntity> users, string externalUserId, CancellationToken cancellationToken) =>
        users.Where(u => u.ExternalUserId == externalUserId)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);

    // For one user acting on another (follow, favourite): both internal IDs, or null for whichever isn't there
    public static async Task<(Guid? UserId, Guid? TargetId)> FindPairAsync(this IQueryable<UserEntity> users, string externalUserId,
        string targetExternalUserId, CancellationToken cancellationToken) =>
        (await users.FindIdByExternalIdAsync(externalUserId, cancellationToken), await users.FindIdByExternalIdAsync(targetExternalUserId, cancellationToken));
}
