using System.Net.Sockets;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Scanning;

namespace FeedFabric.Application.Tests.Scanning;

// Runs against clamd from docker compose (port 3310). Skipped when it isn't up yet; its first start takes a few minutes.
public class ClamAvScannerTests
{
    private static readonly Lazy<Task<bool>> Available = new(async () =>
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", 3310).WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    private static async Task<ClamAvScanner> ScannerAsync()
    {
        Assert.SkipUnless(await Available.Value, "clamd isn't reachable on localhost:3310. Start it: docker compose up -d clamav");
        return new ClamAvScanner(Options.Create(new MalwareScanningOptions()));
    }

    [Fact]
    public async Task Finds_the_eicar_test_file()
    {
        var scanner = await ScannerAsync();

        var result = await scanner.ScanAsync(new MemoryStream(TestFiles.Eicar()), TestContext.Current.CancellationToken);

        Assert.False(result.IsClean);
        Assert.Contains("Eicar", result.Signature, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Passes_a_clean_image()
    {
        var scanner = await ScannerAsync();

        var result = await scanner.ScanAsync(new MemoryStream(TestFiles.Jpeg(300, 200)), TestContext.Current.CancellationToken);

        Assert.True(result.IsClean);
    }

    [Fact]
    public async Task Throws_rather_than_passing_a_file_when_clamd_is_unreachable()
    {
        // Nothing listens on port 1, so this needs no clamd running
        var scanner = new ClamAvScanner(Options.Create(new MalwareScanningOptions { ClamAv = new ClamAvOptions { Port = 1 } }));

        await Assert.ThrowsAsync<MalwareScanException>(() =>
            scanner.ScanAsync(new MemoryStream(TestFiles.Jpeg(10, 10)), TestContext.Current.CancellationToken));
    }
}
