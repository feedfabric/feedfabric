using SkiaSharp;

namespace FeedFabric.Application.Services.Media;

// Rebuilds an image from its pixels: decode, apply the camera's rotation, resize, encode a brand-new file.
// Nothing from the original survives except pixels, so EXIF (including GPS location), comments, embedded
// thumbnails and anything appended to the file are gone. Run only on content that has already been scanned.
public static class ImageSanitizer
{
    public static SanitizedImage Process(byte[] data, MediaOptions options)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data))
            ?? throw new InvalidImageException(MediaRejections.Unreadable);

        // From the header only: refuse decompression bombs before allocating anything
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new InvalidImageException(MediaRejections.Unreadable);
        }
        if (info.Width > options.MaxDimension || info.Height > options.MaxDimension || (long)info.Width * info.Height > options.MaxPixels)
        {
            throw new InvalidImageException(MediaRejections.ImageTooBig);
        }

        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidImageException(MediaRejections.Unreadable);
        using var upright = Orient(decoded, codec.EncodedOrigin);

        // Keep PNG only where transparency matters; everything else becomes JPEG
        var keepsAlpha = codec.EncodedFormat == SKEncodedImageFormat.Png && info.AlphaType != SKAlphaType.Opaque;
        var format = keepsAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;

        return new SanitizedImage(
            Encode(upright, options.LargeEdge, format),
            Encode(upright, options.ThumbnailEdge, format));
    }

    private static ImageVariant Encode(SKBitmap source, int maxEdge, SKEncodedImageFormat format)
    {
        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = scale < 1
            ? source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        using var image = SKImage.FromBitmap(resized ?? source);
        using var encoded = image.Encode(format, 85) ?? throw new InvalidImageException(MediaRejections.Unreadable);

        return format == SKEncodedImageFormat.Png
            ? new ImageVariant(encoded.ToArray(), "image/png", "png", width, height)
            : new ImageVariant(encoded.ToArray(), "image/jpeg", "jpg", width, height);
    }

    // Draws the pixels the way the camera meant them to be seen, since the orientation tag is about to be dropped
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }

        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swaps ? source.Height : source.Width, swaps ? source.Width : source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(result.Width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(result.Width, result.Height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, result.Height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(result.Width, result.Height);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(-90);
                break;
        }
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}

public sealed record SanitizedImage(ImageVariant Large, ImageVariant Thumbnail);

public sealed record ImageVariant(byte[] Data, string ContentType, string Extension, int Width, int Height);

// Reason is one of MediaRejections
public sealed class InvalidImageException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
