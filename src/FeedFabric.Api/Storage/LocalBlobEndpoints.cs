using Microsoft.AspNetCore.Http.Features;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Api.Storage;

// Serves LocalBlobStorage's signed read and upload URLs, the local stand-in for S3 presigned URLs.
// Only mapped when BlobStorage:Provider is Local. The signature is the authorization, as with S3.
public static class LocalBlobEndpoints
{
    public static void MapLocalBlobEndpoints(this WebApplication app)
    {
        app.MapGet("/blobs/{**key}", async (string key, long expires, string sig, LocalBlobStorage storage, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!storage.Verify(LocalBlobOperation.Read, key, "", expires, sig))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var blob = await storage.GetAsync(key, cancellationToken);
            if (blob is null)
            {
                return Results.NotFound();
            }

            // Uploaded files are untrusted: never let a browser sniff them into HTML or run scripts from them
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "sandbox";
            return Results.Stream(blob.Content, blob.ContentType);
        });

        app.MapPut("/blobs/{**key}", async (string key, long expires, string sig, string contentType, LocalBlobStorage storage,
            HttpRequest request, CancellationToken cancellationToken) =>
        {
            if (!storage.Verify(LocalBlobOperation.Upload, key, contentType, expires, sig))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            // Same rule as an S3 presigned PUT: the Content-Type signed into the URL is the only one accepted
            if (!string.Equals(request.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            // Uploads are bigger than the API's usual body limit (Kestrel:Limits:MaxRequestBodySize)
            var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeLimit is { IsReadOnly: false })
            {
                sizeLimit.MaxRequestBodySize = storage.MaxUploadBytes + 1;
            }
            if (request.ContentLength > storage.MaxUploadBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            // Content-Length can be missing or wrong, so count what's actually read too
            await using var limited = new MaxLengthStream(request.Body, storage.MaxUploadBytes);
            try
            {
                await storage.PutAsync(key, limited, contentType, cancellationToken);
            }
            catch (MaxLengthStream.TooLongException)
            {
                await storage.DeleteAsync(key, cancellationToken);
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            return Results.Ok();
        });
    }

    // Reads through to the inner stream and throws once more than maxBytes have been read
    private sealed class MaxLengthStream(Stream inner, long maxBytes) : Stream
    {
        private long read;

        public sealed class TooLongException : Exception;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            read += count;
            return read > maxBytes ? throw new TooLongException() : count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
