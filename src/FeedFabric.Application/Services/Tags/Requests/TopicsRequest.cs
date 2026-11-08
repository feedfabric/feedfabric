using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Tags;

// Body for PUT /admin/topics: the whole set, replacing the one in use
public class TopicsRequest
{
    // Topic name to keywords; empty tags posts with their hashtags only
    [Required(ErrorMessage = ErrorCodes.Required)]
    public Dictionary<string, List<string>> Topics { get; init; } = [];

    // The library set it started from, if any, so the admin panel can say so
    public string? Library { get; init; }
}

// Body for POST /admin/topics/preview: which topics some text would get
public class TopicPreviewRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(5000, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string Text { get; init; } = "";

    // A draft set to try; the one in use when left out
    public Dictionary<string, List<string>>? Topics { get; init; }
}
