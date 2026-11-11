using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FeedFabric.Application.Services.ApiKeys;

public sealed record ApiKeySummary(Guid Id, string Name, string Prefix, DateTime CreatedAt, string? CreatedBy, DateTime? LastUsedAt,
    DateTime? RevokedAt, string? RevokedBy)
{
    public static ApiKeySummary From(ApiKeyEntity key) =>
        new(key.Id, key.Name, key.Prefix, key.CreatedAt, key.CreatedBy, key.LastUsedAt, key.RevokedAt, key.RevokedBy);
}

// The summary, plus the key itself: only ever returned when it's made
public sealed record CreatedApiKey(ApiKeySummary Summary, string Key);

// API keys in the database, by hash. Staff make and revoke them; ApiKeyAuthentication checks them.
public class ApiKeyStore(AppDbContext db, TimeProvider timeProvider)
{
    // Every key starts with this, so a key is recognisable in a header, a config file or a leaked-secret scan
    public const string KeyPrefix = "ffk_";
    public const int MaxNameLength = 80;
    public const int PrefixLength = 12;

    public async Task<List<ApiKeySummary>> ListAsync(CancellationToken cancellationToken)
    {
        var keys = await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken);
        return [.. keys.Select(ApiKeySummary.From)];
    }

    public async Task<CreatedApiKey> CreateAsync(string name, string createdBy, CancellationToken cancellationToken)
    {
        // 32 random bytes: nothing to gain from a slow hash, since there's nothing to guess
        var key = KeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var entity = new ApiKeyEntity
        {
            Name = name.Trim(),
            Prefix = key[..PrefixLength],
            Hash = Hash(key),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = createdBy,
        };
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedApiKey(ApiKeySummary.From(entity), key);
    }

    // The key's hash, for forgetting its cached check; null when there's no such key. Revoking a revoked key leaves
    // it as it was.
    public async Task<string?> RevokeAsync(Guid id, string revokedBy, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (key is null)
        {
            return null;
        }
        if (key.RevokedAt is null)
        {
            key.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
            key.RevokedBy = revokedBy;
            await db.SaveChangesAsync(cancellationToken);
        }
        return key.Hash;
    }

    // The ID of the key with this hash, if it isn't revoked, noting that it was used
    public async Task<Guid?> CheckAsync(string hash, CancellationToken cancellationToken)
    {
        var id = await db.ApiKeys.AsNoTracking().Where(k => k.Hash == hash && k.RevokedAt == null).Select(k => (Guid?)k.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (id is not null)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await db.ApiKeys.Where(k => k.Id == id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), cancellationToken);
        }
        return id;
    }

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
