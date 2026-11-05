using System.Text;
using SkiaSharp;

namespace FeedFabric.Application.Tests;

public static class TestFiles
{
    // The standard EICAR antivirus test file. Built from pieces so this source file itself doesn't trip a scanner.
    public static byte[] Eicar() => Encoding.ASCII.GetBytes(
        string.Concat(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$", "EICAR-STANDARD-", "ANTIVIRUS-TEST-", "FILE!$H+H*"));

    // A JPEG whose left half is red and right half is blue
    public static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(0, 0, width / 2f, height, new SKPaint { Color = SKColors.Red });
        }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    public const string SecretDescription = "HomeAddress-51.5074N";

    // Inserts an EXIF block after the JPEG's start marker: an orientation tag, a GPS section and a description
    // that stands in for private metadata
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte>();
        void U16(int v) { tiff.Add((byte)v); tiff.Add((byte)(v >> 8)); }
        void U32(int v) { U16(v & 0xFFFF); U16(v >> 16); }

        var description = Encoding.ASCII.GetBytes(SecretDescription + "\0");
        const int gpsOffset = 50, descriptionOffset = 68;
        tiff.AddRange("II"u8.ToArray()); U16(42); U32(8);
        U16(3);
        U16(0x010E); U16(2); U32(description.Length); U32(descriptionOffset); // ImageDescription
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);              // Orientation
        U16(0x8825); U16(4); U32(1); U32(gpsOffset);                         // GPS section
        U32(0);
        U16(1);
        U16(0x0001); U16(2); U32(2); tiff.AddRange("N\0\0\0"u8.ToArray());  // GPSLatitudeRef
        U32(0);
        tiff.AddRange(description);

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        return [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, .. jpeg[2..]];
    }

    public static bool Contains(byte[] haystack, string text) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;
}
