using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Scanning;

// Streams content to clamd with its INSTREAM command: each chunk is a 4-byte big-endian length then the bytes,
// and a zero length ends the stream. clamd replies "stream: OK" or "stream: <signature> FOUND".
public class ClamAvScanner(IOptions<MalwareScanningOptions> options) : IMalwareScanner
{
    private const string FoundSuffix = " FOUND";
    private const string ReplyPrefix = "stream: ";

    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        var settings = options.Value.ClamAv;
        var start = content.CanSeek ? content.Position : 0;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ScanOnceAsync(content, cancellationToken);
            }
            catch (MalwareScanException ex) when (ex.InnerException is SocketException && content.CanSeek && attempt < settings.ConnectRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
                content.Position = start;
            }
        }
    }

    private async Task<ScanResult> ScanOnceAsync(Stream content, CancellationToken cancellationToken)
    {
        var settings = options.Value.ClamAv;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);

        string reply;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            await using var network = client.GetStream();

            // The "z" prefix means commands and replies end with a NUL byte
            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var buffer = new byte[4 + settings.ChunkBytes];
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(4, settings.ChunkBytes), timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(buffer, read);
                await network.WriteAsync(buffer.AsMemory(0, 4 + read), timeout.Token);
            }
            await network.WriteAsync(new byte[4], timeout.Token);

            reply = await ReadReplyAsync(network, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MalwareScanException($"Couldn't scan with clamd at {settings.Host}:{settings.Port}: {ex.Message}", ex);
        }

        if (reply == $"{ReplyPrefix}OK")
        {
            return ScanResult.Clean;
        }
        if (reply.StartsWith(ReplyPrefix, StringComparison.Ordinal) && reply.EndsWith(FoundSuffix, StringComparison.Ordinal))
        {
            return ScanResult.Infected(reply[ReplyPrefix.Length..^FoundSuffix.Length]);
        }
        // e.g. "INSTREAM size limit exceeded. ERROR": no verdict, so it isn't clean
        throw new MalwareScanException($"clamd didn't give a verdict: {reply}");
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.ClamAv;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            await using var network = client.GetStream();
            await network.WriteAsync("zPING\0"u8.ToArray(), timeout.Token);
            return await ReadReplyAsync(network, timeout.Token) == "PONG";
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<string> ReadReplyAsync(NetworkStream network, CancellationToken cancellationToken)
    {
        var reply = new MemoryStream();
        var buffer = new byte[256];
        int read;
        while ((read = await network.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var end = Array.IndexOf(buffer, (byte)0, 0, read);
            reply.Write(buffer, 0, end >= 0 ? end : read);
            if (end >= 0)
            {
                break;
            }
        }
        return Encoding.ASCII.GetString(reply.ToArray()).Trim();
    }
}
