using FeedFabric.Application.Services;
using FeedFabric.Application.Services.Safety;

public class PostEntity : EntityBase
{
    public required string Content {get;set;}

    /// <summary>
    /// The Id from the UserEntity
    /// </summary>
    public required Guid AuthorId {get;init;}

    public DateTime CreatedAt {get;init;}

    // Set on a repost: the original post it shares. Reposts have no content of their own.
    public Guid? RepostOfId {get;init;}

    // You have one hour to edit a post, until it can become uneditable
    public DateTime? EditedAt {get;set;}

    // Soft delete: set instead of removing the row
    public DateTime? DeletedAt {get;set;}

    // Held for review, or removed by a reviewer: see ContentStatus
    public ContentStatus Status {get;set;}

    // Structured data from the host's app, checked against its data type (DataTypeEntity): the type's key, the
    // version it was checked against, and the data as JSON
    public string? DataType {get;set;}

    public int? DataVersion {get;set;}

    public string? Data {get;set;}
}
