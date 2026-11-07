using System.Security.Cryptography;
using System.Text.Json;

namespace FeedFabric.Application.Services.Storage;

// Model files from blob storage, checked against the SHA-256 their publisher recorded (meta.json's "sha256", or
// latest.json's for the ranker). A mismatch is a publish still in progress or a damaged file: it reads as missing,
// so the model isn't used and the next attempt tries again.
public static class ModelFiles
{
    public static async Task<MemoryStream> ReadAsync(IBlobStorage storage, string key, string? sha256, string what,
        CancellationToken cancellationToken)
    {
        await using var file = await storage.GetAsync(key, cancellationToken)
            ?? throw new ModelNotFoundException($"No {what} at {key}: run make models.");
        var copy = new MemoryStream();
        await file.Content.CopyToAsync(copy, cancellationToken);
        if (sha256 is not null)
        {
            var actual = Convert.ToHexStringLower(SHA256.HashData(copy.GetBuffer().AsSpan(0, (int)copy.Length)));
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelNotFoundException($"{key} doesn't match the checksum it was published with (a publish in progress, or a damaged file).");
            }
        }
        copy.Position = 0;
        return copy;
    }

    // The checksum meta.json lists for one of the model's files; null for models published before checksums
    public static string? Checksum(JsonElement meta, string name) =>
        meta.TryGetProperty("sha256", out var sums) && sums.TryGetProperty(name, out var sum) ? sum.GetString() : null;
}
