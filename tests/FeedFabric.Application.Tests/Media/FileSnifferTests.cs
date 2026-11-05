using FeedFabric.Application.Services.Media;

namespace FeedFabric.Application.Tests.Media;

public class FileSnifferTests
{
    [Fact]
    public void Detects_jpeg_png_and_webp_from_their_bytes()
    {
        Assert.Equal("image/jpeg", FileSniffer.Detect(TestFiles.Jpeg(10, 10)));
        Assert.Equal("image/png", FileSniffer.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]));
        Assert.Equal("image/webp", FileSniffer.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><body>hi</body></html>")]
    [InlineData("MZ\u0090\0 not really an exe")]
    [InlineData("")]
    public void Anything_else_is_not_an_allowed_image(string content)
    {
        Assert.Null(FileSniffer.Detect(System.Text.Encoding.UTF8.GetBytes(content)));
    }
}
