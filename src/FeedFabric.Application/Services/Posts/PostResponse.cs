namespace FeedFabric.Application.Services.Posts;

public class PostResponse
{
    public required Guid Id {get;set;}

    public required Guid AuthorId {get;set;}

    public required string Content {get;set;}

    public DateTime CreatedAt {get;set;}

    public DateTime? EditedAt {get;set;}

    public static PostResponse From(PostEntity post) => new()
    {
        Id = post.Id,
        AuthorId = post.AuthorId,
        Content = post.Content,
        CreatedAt = post.CreatedAt,
        EditedAt = post.EditedAt,
    };
}
