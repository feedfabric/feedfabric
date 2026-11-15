using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedFabric.Sdk;

// The API's shapes, as the member routes return them. IDs are UUIDs; times are UTC.

/// <summary>Why a post or comment is reported.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportReason>))]
public enum ReportReason
{
    [JsonStringEnumMemberName("spam")] Spam,
    [JsonStringEnumMemberName("harassment")] Harassment,
    [JsonStringEnumMemberName("hate")] Hate,
    [JsonStringEnumMemberName("threat")] Threat,
    [JsonStringEnumMemberName("sexual")] Sexual,
    [JsonStringEnumMemberName("self_harm")] SelfHarm,
    [JsonStringEnumMemberName("other")] Other,
}

/// <summary>Which of the member's own posts come first.</summary>
public enum PostOrder
{
    Newest,
    Oldest,
}

/// <summary>One of a post's tags. Source is "hashtag", "inferred" or "app" (set by your app in a post's Tags).</summary>
public sealed record Tag
{
    [JsonPropertyName("tag")] public string Value { get; init; } = "";
    public string Source { get; init; } = "";
}

public sealed record PostMedia(Guid Id, string Url, string ThumbnailUrl, int Width, int Height);

/// <summary>A post's structured data, of a type in GET /data-types. A <see cref="DataType{T}"/> reads it as your own type.</summary>
/// <param name="Version">The data type's version the value was checked against</param>
public sealed record PostData(string Type, int Version, JsonElement Value);

/// <summary>A post's data as you send it; <see cref="DataType{T}.Of"/> builds one.</summary>
public sealed record PostDataInput(string Type, JsonElement Value);

public sealed record RankingFactor(string Label, double Value, string? Detail);

/// <summary>Only on For You posts: why the post is where it is.</summary>
public sealed record PostRanking(string Ranker, int Position, double Score, bool Explored, IReadOnlyList<RankingFactor> Factors,
    IReadOnlyDictionary<string, double>? Features);

/// <summary>A post as feeds return it.</summary>
public sealed record Post
{
    public Guid Id { get; init; }
    public Guid AuthorId { get; init; }
    public string AuthorExternalUserId { get; init; } = "";
    public string AuthorUsername { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? EditedAt { get; init; }
    public Guid? RepostOfId { get; init; }
    public Post? RepostOf { get; init; }
    public bool IsHeld { get; init; }
    public IReadOnlyList<Tag> Tags { get; init; } = [];
    public IReadOnlyList<PostMedia> Media { get; init; } = [];
    public PostData? Data { get; init; }
    public PostRanking? Ranking { get; init; }
    public int Likes { get; init; }
    public int Comments { get; init; }
    public int Reposts { get; init; }
    public bool Liked { get; init; }
    public bool Reposted { get; init; }
}

/// <summary>A post as creating, editing and listing your own posts return it.</summary>
public sealed record SavedPost
{
    public Guid Id { get; init; }
    public Guid AuthorId { get; init; }
    public string Content { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? EditedAt { get; init; }
    public Guid? RepostOfId { get; init; }
    public bool IsHeld { get; init; }
    public PostData? Data { get; init; }
}

/// <param name="NextCursor">Pass back as the cursor for the next page; null at the end</param>
public sealed record FeedPage(IReadOnlyList<Post> Posts, string? NextCursor, string? Source);

public sealed record ForYouPage(IReadOnlyList<Post> Posts, string? NextCursor, string Ranker, string? Profile, int Candidates);

public sealed record OwnPostsPage(IReadOnlyList<SavedPost> Posts, string? NextCursor);

/// <param name="Content">Null once deleted or removed</param>
public sealed record Comment(Guid Id, Guid PostId, Guid? ParentCommentId, Guid AuthorId, string AuthorExternalUserId, string AuthorUsername,
    string? Content, bool IsDeleted, bool IsHeld, DateTime CreatedAt, int ReplyCount);

public sealed record CommentPage(IReadOnlyList<Comment> Comments, string? NextCursor);

public sealed record Mention(Guid Id, DateTime CreatedAt, string AuthorExternalUserId, string AuthorUsername, Guid PostId, Guid? CommentId,
    string Content);

public sealed record MentionPage(IReadOnlyList<Mention> Mentions, string? NextCursor);

public sealed record TrendingTag(string Tag, int Posts);

public sealed record Favourite(string ExternalUserId, string Username, string? DisplayName, string? AvatarUrl, DateTime AddedAt);

public sealed record Upload(Guid MediaId, string UploadUrl, string ContentType, DateTime ExpiresAt);

/// <summary>An upload. Status is "awaitingUpload", "processing", "ready" or "rejected".</summary>
public sealed record Media(Guid Id, string Status, string? RejectionReason, int? Width, int? Height, string? Url, string? ThumbnailUrl);

/// <param name="Schema">The JSON Schema posts' data of this type is checked against</param>
public sealed record DataTypeInfo(string Key, string Label, string? Description, int Version, JsonElement Schema, DateTime UpdatedAt,
    string? UpdatedBy);

/// <summary>How many signals were counted (repeats within 30 minutes aren't).</summary>
public sealed record SignalsCounted(int Counted);

public sealed record DwellView(Guid PostId, int Ms);

public sealed record Me(string ExternalUserId);

/// <summary>A post to create or edit.</summary>
public sealed record PostInput
{
    /// <summary>Required unless the post has data.</summary>
    public string? Content { get; init; }

    /// <summary>Your app's own tags, written like hashtags without the #; up to 10. On an edit, null keeps the post's.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Ready images from <see cref="MediaUploads.UploadAsync"/>, in order. Only when creating.</summary>
    public IReadOnlyList<Guid>? MediaIds { get; init; }

    /// <summary>Structured data of a registered type; <see cref="DataType{T}.Of"/> builds it. On an edit, null keeps the post's.</summary>
    public PostDataInput? Data { get; init; }
}

/// <summary>Which of the member's own posts to list.</summary>
public sealed record OwnPostsQuery
{
    /// <summary>Only posts with data of this type.</summary>
    public string? Type { get; init; }
    public PostOrder? Order { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }

    // Post IDs are time-ordered (UUIDv7): these bound the range
    public Guid? Gt { get; init; }
    public Guid? Gte { get; init; }
    public Guid? Lt { get; init; }
    public Guid? Lte { get; init; }
}

/// <summary>A member's profile: used when FeedFabric first sees them, and refreshing theirs at most hourly.</summary>
public sealed record MemberProfile(string? Username = null, string? DisplayName = null, string? AvatarUrl = null);
