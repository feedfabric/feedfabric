using System.ComponentModel.DataAnnotations;
using FeedFabric.Application.Errors;

namespace FeedFabric.Application.Services.Media;

// Body for POST /media/uploads
public class CreateUploadRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    public string ContentType { get; init; } = "";

    // Bytes the client is about to upload; checked again against the real file
    [Range(1, long.MaxValue, ErrorMessage = ErrorCodes.OutOfRange + ";min={1};max={2}")]
    public long Size { get; init; }
}
