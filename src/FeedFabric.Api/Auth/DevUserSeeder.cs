using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Auth;

// Local dev users to match X-Dev-User 1-3. EF runs this after `make migrate`.
public static class DevUserSeeder
{
    public static readonly string[] ExternalUserIds = ["1", "2", "3"];

    public static void Seed(DbContext context, bool _)
    {
        var users = context.Set<UserEntity>();
        var existing = users.Where(u => ExternalUserIds.Contains(u.ExternalUserId)).Select(u => u.ExternalUserId).ToList();
        users.AddRange(MissingUsers(existing));
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool _, CancellationToken cancellationToken)
    {
        var users = context.Set<UserEntity>();
        var existing = await users.Where(u => ExternalUserIds.Contains(u.ExternalUserId)).Select(u => u.ExternalUserId).ToListAsync(cancellationToken);
        users.AddRange(MissingUsers(existing));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<UserEntity> MissingUsers(List<string> existing) =>
        ExternalUserIds.Except(existing).Select(id => new UserEntity
        {
            ExternalUserId = id,
            Username = $"dev{id}",
            SyncedAt = DateTime.UtcNow,
        });
}
