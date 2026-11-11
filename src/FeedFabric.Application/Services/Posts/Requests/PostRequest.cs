using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.Tags;

namespace FeedFabric.Application.Services.Posts;

// Body for creating and editing a post
public class PostRequest : IValidatableObject
{
    public const int MaxContentLength = 5000;
    public const int MaxTags = 10;

    // Required, except on a post that carries data: a weight reading needs no words
    [MaxLength(MaxContentLength, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string Content {get;init;} = "";

    // Ready images from POST /media/uploads, in display order. Only used when creating a post.
    public List<Guid>? MediaIds {get;set;}

    // Tags the app sets, as well as the #hashtags in the text: written like hashtags, without the #. On an edit,
    // leaving them out keeps the ones the post has.
    [MaxLength(MaxTags, ErrorMessage = ErrorCodes.OutOfRange + ";max={1}")]
    public List<string>? Tags {get;init;}

    // Structured data of a type staff registered (GET /data-types), checked against its schema. On an edit, leaving it
    // out keeps the data the post has.
    public PostDataRequest? Data {get;init;}

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Content) && Data is null)
        {
            yield return new ValidationResult(ErrorCodes.Required, [nameof(Content)]);
        }
        if (Tags?.Any(tag => !TextTokens.IsTag(tag)) == true)
        {
            yield return new ValidationResult(ErrorCodes.InvalidValue, [nameof(Tags)]);
        }
    }
}

public class PostDataRequest
{
    // A data type's key, e.g. "weight-reading"
    [Required(ErrorMessage = ErrorCodes.Required)]
    public string Type {get;init;} = "";

    // The data itself: a JSON object its type's schema accepts
    public JsonElement Value {get;init;}
}
