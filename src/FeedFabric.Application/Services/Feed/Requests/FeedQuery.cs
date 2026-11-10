using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Feed;

public enum FeedOrder
{
    Newest,
    Oldest,
}

// Post IDs are UUIDv7, so comparing IDs compares creation times, using the (AuthorId, Id) index
public class FeedQuery
{
    public Guid? Gt {get;set;}

    public Guid? Gte {get;set;}

    public Guid? Lt {get;set;}

    public Guid? Lte {get;set;}

    // The previous page's NextCursor; continues in the same Order
    public Guid? Cursor {get;set;}

    public FeedOrder Order {get;set;} = FeedOrder.Newest;

    // Only posts with data of this type (GET /data-types), e.g. "weight-reading"
    [MaxLength(40, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string? Type {get;set;}

    [Range(1, 100, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")]
    public int Limit {get;set;} = 20;
}
