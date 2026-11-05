using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Feed;

// Newest first. Pass the previous page's NextCursor to continue.
public class HomeFeedQuery
{
    public Guid? Cursor {get;set;}

    [Range(1, 100, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")]
    public int Limit {get;set;} = 20;
}
