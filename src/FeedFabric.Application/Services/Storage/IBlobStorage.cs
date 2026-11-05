namespace FeedFabric.Application.Services.Storage;

// File storage behind one interface, so code that stores files never knows where they go.
// BlobStorageOptions.Provider picks the implementation: Local for dev and tests, S3 for anything S3-compatible.
// Keys are paths like "uploads/2026/11/<id>.jpg": letters, digits, '-', '_', '.' and '/' only; see BlobKey.
public interface IBlobStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    // Null if there's nothing at the key
    Task<BlobDownload?> GetAsync(string key, CancellationToken cancellationToken);

    // Deleting a key that doesn't exist succeeds
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    // A short-lived URL a client can download from directly
    Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken);

    // A short-lived URL a client can PUT the file to directly, with exactly this Content-Type,
    // so large files never pass through the API
    Task<Uri> GetUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken);
}

// Dispose it to release the underlying stream or connection
public sealed record BlobDownload(Stream Content, string ContentType, long Length) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
