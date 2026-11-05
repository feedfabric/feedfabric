using FeedFabric.Application.Services.Media;
using SkiaSharp;

namespace FeedFabric.Application.Tests.Media;

public class ImageSanitizerTests
{
    private static readonly MediaOptions Options = new() { LargeEdge = 2048, ThumbnailEdge = 100 };

    [Fact]
    public void Strips_exif_including_gps_and_private_text()
    {
        var upload = TestFiles.WithExif(TestFiles.Jpeg(300, 200), orientation: 1);
        Assert.True(TestFiles.Contains(upload, TestFiles.SecretDescription));

        var result = ImageSanitizer.Process(upload, Options);

        foreach (var variant in new[] { result.Large, result.Thumbnail })
        {
            Assert.False(TestFiles.Contains(variant.Data, "Exif"));
            Assert.False(TestFiles.Contains(variant.Data, TestFiles.SecretDescription));
        }
    }

    [Fact]
    public void Applies_the_camera_rotation_before_dropping_the_tag()
    {
        // Orientation 6: the camera was turned 90 degrees clockwise, so the stored landscape image is shown as portrait
        var result = ImageSanitizer.Process(TestFiles.WithExif(TestFiles.Jpeg(300, 200), orientation: 6), Options);

        Assert.Equal((200, 300), (result.Large.Width, result.Large.Height));
        using var bitmap = SKBitmap.Decode(result.Large.Data);
        Assert.Equal((200, 300), (bitmap.Width, bitmap.Height));
        // The stored image's red left half ends up on top
        Assert.True(bitmap.GetPixel(100, 20).Red > 200 && bitmap.GetPixel(100, 20).Blue < 80);
        Assert.True(bitmap.GetPixel(100, 280).Blue > 200 && bitmap.GetPixel(100, 280).Red < 80);
    }

    [Fact]
    public void Resizes_to_the_largest_edge_and_keeps_small_images_as_they_are()
    {
        var result = ImageSanitizer.Process(TestFiles.Jpeg(400, 200), new MediaOptions { LargeEdge = 1000, ThumbnailEdge = 100 });

        Assert.Equal((400, 200), (result.Large.Width, result.Large.Height));
        Assert.Equal((100, 50), (result.Thumbnail.Width, result.Thumbnail.Height));
        Assert.Equal("image/jpeg", result.Large.ContentType);
    }

    [Fact]
    public void Refuses_images_over_the_pixel_limit_before_decoding_them()
    {
        var ex = Assert.Throws<InvalidImageException>(() =>
            ImageSanitizer.Process(TestFiles.Jpeg(300, 200), new MediaOptions { MaxPixels = 10_000 }));

        Assert.Equal(MediaRejections.ImageTooBig, ex.Reason);
    }

    [Fact]
    public void Refuses_files_that_only_look_like_images()
    {
        byte[] fake = [0xFF, 0xD8, 0xFF, .. "not actually a jpeg"u8.ToArray()];

        var ex = Assert.Throws<InvalidImageException>(() => ImageSanitizer.Process(fake, Options));

        Assert.Equal(MediaRejections.Unreadable, ex.Reason);
    }
}
