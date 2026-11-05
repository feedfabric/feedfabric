using FeedFabric.Application.Services;

public class PostEntity : EntityBase
{
    public required string Content {get;set;}

    /// <summary>
    /// The Id from the UserEntity
    /// </summary>
    public required Guid AuthorId {get;set;}

    public DateTime CreatedAt {get;set;}

    // You have one hour to edit a post, until it can become uneditable
    public DateTime? EditedAt {get;set;}

    // Soft delete: set instead of removing the row
    public DateTime? DeletedAt {get;set;}
}
