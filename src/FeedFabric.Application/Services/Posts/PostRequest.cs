using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Posts;

// Body for creating and editing a post
public class PostRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    public string Content {get;set;} = "";

    // Ready images from POST /media/uploads, in display order. Only used when creating a post.
    public List<Guid>? MediaIds {get;set;}
}
