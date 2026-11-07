using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Comments;

// Body for commenting on a post; set ParentCommentId to reply to a comment instead
public class CommentRequest
{
    public const int MaxContentLength = 2000;

    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(MaxContentLength, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string Content { get; init; } = "";

    public Guid? ParentCommentId { get; init; }
}
