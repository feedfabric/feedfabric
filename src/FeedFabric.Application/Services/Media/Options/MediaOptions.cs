namespace FeedFabric.Application.Services.Media;

public class MediaOptions
{
    public const string Section = "Media";

    // Types clients may upload; the worker also checks the bytes really are one of these
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
    };

    // Kept under clamd's default 25 MB StreamMaxLength so every allowed file can be scanned
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;

    // Checked from the header before anything is decoded, so a small file can't expand into a huge bitmap
    public int MaxDimension { get; set; } = 12_000;

    public long MaxPixels { get; init; } = 50_000_000;

    // Longest edge of the rebuilt image and of its thumbnail
    public int LargeEdge { get; init; } = 2048;

    public int ThumbnailEdge { get; init; } = 400;

    public int MaxPerPost { get; set; } = 4;

    public TimeSpan UploadUrlLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan ReadUrlLifetime { get; set; } = TimeSpan.FromHours(1);
}
