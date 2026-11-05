namespace FeedFabric.Application.Services.Storage;

public class BlobStorageOptions
{
    public const string Section = "BlobStorage";

    // "Local" or "S3"
    public string Provider { get; set; } = "Local";

    public LocalBlobStorageOptions Local { get; set; } = new();

    public S3BlobStorageOptions S3 { get; set; } = new();
}

public class LocalBlobStorageOptions
{
    // Folder the files are written to. A relative path is relative to the app's build output (bin/Debug/net10.0),
    // so the API and the worker can share one folder however they're started.
    public string Root { get; set; } = "blobs";

    // Where the API serves /blobs, used to build read and upload URLs. Empty gives relative URLs (/blobs/...),
    // which work from any origin that serves or proxies the API.
    public string PublicBaseUrl { get; set; } = "http://localhost:5161";

    // Signs read and upload URLs so they can't be guessed or altered. Any long random string; dev only.
    public string SigningKey { get; set; } = "";

    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
}

public class S3BlobStorageOptions
{
    public string Bucket { get; set; } = "";

    public string Region { get; set; } = "us-east-1";

    // Leave empty for AWS. Set for S3-compatible storage: LocalStack, Cloudflare R2, MinIO, DigitalOcean Spaces.
    public string? ServiceUrl { get; set; }

    // The address clients use, when it differs from ServiceUrl (e.g. the API reaches LocalStack at
    // http://localstack:4566 inside Docker but browsers need http://localhost:4566). Signed URLs use this host.
    public string? PublicServiceUrl { get; set; }

    // Leave empty to use the default AWS credential chain (environment, profile, IAM role)
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    // Path-style URLs (host/bucket/key); most S3-compatible services need this
    public bool ForcePathStyle { get; set; }

    // Create the bucket on startup if it's missing. For local emulators; production buckets are provisioned separately.
    public bool CreateBucket { get; set; }
}
