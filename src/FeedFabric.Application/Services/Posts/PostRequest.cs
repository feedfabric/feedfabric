using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Posts;

// Body for creating and editing a post
public class PostRequest
{
    public const int MaxContentLength = 5000;

    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(MaxContentLength, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string Content {get;set;} = "";

    // Ready images from POST /media/uploads, in display order. Only used when creating a post.
    public List<Guid>? MediaIds {get;set;}
}
