using System.Diagnostics;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Scanning;
using FeedFabric.Worker.Events.Kafka;

namespace FeedFabric.Worker.Tests;

public class ClamAvScannerTests
{
    [Fact]
    public async Task Retries_a_refused_connection_then_fails_as_transient()
    {
        var scanner = new ClamAvScanner(Options.Create(new MalwareScanningOptions
        {
            ClamAv = new ClamAvOptions { Host = "127.0.0.1", Port = 1, ConnectRetries = 1 },
        }));
        using var content = new MemoryStream([1, 2, 3]);
        var clock = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<MalwareScanException>(() => scanner.ScanAsync(content, TestContext.Current.CancellationToken));

        // One retry, a second after the first refusal
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(1));
        Assert.True(TransientFailures.IsTransient(ex));
    }
}
