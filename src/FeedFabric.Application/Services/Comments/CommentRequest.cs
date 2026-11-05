using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Comments;

// Body for commenting on a post; set ParentCommentId to reply to a comment instead
public class CommentRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    public string Content { get; set; } = "";

    public Guid? ParentCommentId { get; set; }
}
