using FeedFabric.Application.Services.Safety;

namespace FeedFabric.Application.Services.Posts;

public class PostResponse
{
    public required Guid Id {get;init;}

    public required Guid AuthorId {get;set;}

    public required string Content {get;set;}

    public DateTime CreatedAt {get;init;}

    public DateTime? EditedAt {get;set;}

    // Set on a repost: the post it shares
    public Guid? RepostOfId {get;set;}

    // Held for review: saved, but only the author sees it until a reviewer decides
    public bool IsHeld {get;set;}

    public static PostResponse From(PostEntity post) => new()
    {
        Id = post.Id,
        AuthorId = post.AuthorId,
        Content = post.Content,
        CreatedAt = post.CreatedAt,
        EditedAt = post.EditedAt,
        RepostOfId = post.RepostOfId,
        IsHeld = post.Status == ContentStatus.Held,
    };
}
