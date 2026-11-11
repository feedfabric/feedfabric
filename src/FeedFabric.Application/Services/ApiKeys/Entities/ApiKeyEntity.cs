namespace FeedFabric.Application.Services.ApiKeys;

// A key the host's own servers call FeedFabric with, acting for one of its members at a time (the FeedFabric-User
// header). Only a hash of the key is kept: the key itself is shown once, when it's made.
public class ApiKeyEntity : EntityBase
{
    public required string Name { get; set; }

    // The key's first characters, so staff can tell keys apart without the whole thing
    public required string Prefix { get; init; }

    // SHA-256 of the whole key, hex
    public required string Hash { get; init; }

    public DateTime CreatedAt { get; init; }

    // The external user ID of whoever made it
    public string? CreatedBy { get; init; }

    // To the minute or so: a key's use is only written down once its cached check runs out
    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevokedBy { get; set; }
}
