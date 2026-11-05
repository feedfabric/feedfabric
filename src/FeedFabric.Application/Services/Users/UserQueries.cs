using Microsoft.EntityFrameworkCore;

namespace FeedFabric.Application.Services.Users;

public static class UserQueries
{
    // Maps the signed-in user's external ID to the internal UserEntity.Id, or null if they haven't been synced
    public static Task<Guid?> FindIdByExternalIdAsync(this IQueryable<UserEntity> users, string externalUserId, CancellationToken cancellationToken) =>
        users.Where(u => u.ExternalUserId == externalUserId)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
