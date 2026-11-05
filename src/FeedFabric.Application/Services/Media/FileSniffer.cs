namespace FeedFabric.Application.Services.Media;

// The real file type from its first bytes. Names, extensions and Content-Type headers are all client-controlled.
public static class FileSniffer
{
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }
        return null;
    }
}
