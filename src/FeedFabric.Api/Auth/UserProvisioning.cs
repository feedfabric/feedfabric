using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Auth;

// Makes sure every signed-in user has a UserEntity, created from their token on their first request and refreshed
// from later tokens once AuthOptions.ProfileRefresh has passed. A user's checked state is cached, so most requests
// never touch the users table here.
public sealed partial class UserProvisioning(RequestDelegate next, IOptions<AuthOptions> options, IMemoryCache cache, TimeProvider timeProvider)
{
    private const int MaxUsernameLength = 30;

    [GeneratedRegex("[^a-z0-9_]+")]
    private static partial Regex NotUsername();

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var externalUserId = context.User.FindFirst(IdentityClaims.UserId)?.Value;
        if (options.Value.ProvisionUsers && externalUserId is not null && !cache.TryGetValue(CacheKey(externalUserId), out _))
        {
            await EnsureAsync(context, db, externalUserId, context.RequestAborted);
            cache.Set(CacheKey(externalUserId), true, options.Value.ProfileRefresh);
        }
        await next(context);
    }

    private async Task EnsureAsync(HttpContext context, AppDbContext db, string externalUserId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var wanted = context.User.FindFirst(IdentityClaims.Username)?.Value;
        var displayName = context.User.FindFirst(IdentityClaims.DisplayName)?.Value;
        var avatar = context.User.FindFirst(IdentityClaims.Avatar)?.Value;

        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalUserId == externalUserId, cancellationToken);
        if (user is null)
        {
            db.Users.Add(new UserEntity
            {
                ExternalUserId = externalUserId,
                Username = await FreeUsernameAsync(db, Username(wanted, externalUserId), null, cancellationToken),
                DisplayName = displayName,
                AvatarUrl = avatar,
                SyncedAt = now,
            });
        }
        else if (now - user.SyncedAt >= options.Value.ProfileRefresh)
        {
            // Only fields the token actually carries; a token without a picture doesn't clear one
            if (wanted is not null && Username(wanted, externalUserId) is var username && username != user.Username)
            {
                user.Username = await FreeUsernameAsync(db, username, user.Id, cancellationToken);
            }
            user.DisplayName = displayName ?? user.DisplayName;
            user.AvatarUrl = avatar ?? user.AvatarUrl;
            user.SyncedAt = now;
        }
        else
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first requests at once: the other one created the user
            db.ChangeTracker.Clear();
        }
    }

    // @mention-able: lowercase letters, digits and underscores
    private static string Username(string? wanted, string externalUserId)
    {
        var name = NotUsername().Replace((wanted ?? "").ToLowerInvariant(), "_").Trim('_');
        if (name.Length < 2)
        {
            name = "user_" + NotUsername().Replace(externalUserId.ToLowerInvariant(), "");
        }
        return name.Length > MaxUsernameLength ? name[..MaxUsernameLength] : name;
    }

    // The name, or the name with a number on the end if someone else has it
    private static async Task<string> FreeUsernameAsync(AppDbContext db, string name, Guid? self, CancellationToken cancellationToken)
    {
        var taken = (await db.Users.Where(u => u.Username.StartsWith(name) && u.Id != self).Select(u => u.Username).ToListAsync(cancellationToken))
            .ToHashSet();
        if (!taken.Contains(name))
        {
            return name;
        }
        for (var n = 2; ; n++)
        {
            var suffix = $"_{n}";
            var candidate = (name.Length + suffix.Length > MaxUsernameLength ? name[..(MaxUsernameLength - suffix.Length)] : name) + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string CacheKey(string externalUserId) => $"provisioned:{externalUserId}";
}
