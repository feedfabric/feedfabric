using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Storage;

// Files on local disk, for dev and tests. Read and upload URLs point at the API's /blobs endpoint and carry
// an HMAC signature and expiry, so they behave like S3's presigned URLs.
public class LocalBlobStorage : IBlobStorage
{
    private const string ContentTypeSuffix = ".content-type";

    private readonly LocalBlobStorageOptions options;
    private readonly string root;
    private readonly byte[] signingKey;
    private readonly TimeProvider timeProvider;

    public LocalBlobStorage(IOptions<BlobStorageOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value.Local;
        this.timeProvider = timeProvider;
        root = Path.GetFullPath(this.options.Root, AppContext.BaseDirectory);
        signingKey = Encoding.UTF8.GetBytes(this.options.SigningKey.Length >= 32
            ? this.options.SigningKey
            : throw new InvalidOperationException("BlobStorage:Local:SigningKey must be at least 32 characters."));
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write to a temp file then move, so a reader never sees half a file
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var file = File.Create(temp))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        await File.WriteAllTextAsync(path + ContentTypeSuffix, contentType, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<BlobDownload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var contentType = File.Exists(path + ContentTypeSuffix)
            ? await File.ReadAllTextAsync(path + ContentTypeSuffix, cancellationToken)
            : "application/octet-stream";
        var stream = File.OpenRead(path);
        return new BlobDownload(stream, contentType, stream.Length);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        File.Delete(path);
        File.Delete(path + ContentTypeSuffix);
        return Task.CompletedTask;
    }

    public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) =>
        Task.FromResult(SignedUrl(LocalBlobOperation.Read, key, contentType: "", lifetime));

    public Task<Uri> GetUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) =>
        Task.FromResult(SignedUrl(LocalBlobOperation.Upload, key, contentType, lifetime));

    public long MaxUploadBytes => options.MaxUploadBytes;

    // For the /blobs endpoint: true if the URL was signed by this storage for this operation and hasn't expired
    public bool Verify(LocalBlobOperation operation, string key, string contentType, long expires, string signature)
    {
        if (timeProvider.GetUtcNow().ToUnixTimeSeconds() > expires)
        {
            return false;
        }

        var expected = Sign(operation, key, contentType, expires);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    private Uri SignedUrl(LocalBlobOperation operation, string key, string contentType, TimeSpan lifetime)
    {
        BlobKey.Validate(key);
        var expires = timeProvider.GetUtcNow().Add(lifetime).ToUnixTimeSeconds();
        var query = $"op={operation.ToString().ToLowerInvariant()}&expires={expires}&sig={Sign(operation, key, contentType, expires)}";
        if (operation == LocalBlobOperation.Upload)
        {
            query += $"&contentType={Uri.EscapeDataString(contentType)}";
        }
        return new Uri($"{options.PublicBaseUrl.TrimEnd('/')}/blobs/{key}?{query}", UriKind.RelativeOrAbsolute);
    }

    private string Sign(LocalBlobOperation operation, string key, string contentType, long expires)
    {
        var payload = $"{operation}\n{key}\n{contentType}\n{expires.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToHexStringLower(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(payload)));
    }

    // BlobKey.Validate already rules out "..", but check the resolved path too
    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(root, BlobKey.Validate(key)));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"Invalid blob key '{key}'.", nameof(key));
    }
}

public enum LocalBlobOperation
{
    Read,
    Upload,
}
