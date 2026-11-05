using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace FeedFabric.Application.Services.Storage;

// Amazon S3 or any S3-compatible service (LocalStack, Cloudflare R2, MinIO, DigitalOcean Spaces)
public sealed class S3BlobStorage : IBlobStorage, IDisposable
{
    private readonly S3BlobStorageOptions options;
    private readonly IAmazonS3 client;
    // Signs URLs with the host clients will use, which can differ from the one the API talks to
    private readonly IAmazonS3 signer;

    public S3BlobStorage(IOptions<BlobStorageOptions> options)
    {
        this.options = options.Value.S3;
        if (string.IsNullOrEmpty(this.options.Bucket))
        {
            throw new InvalidOperationException("BlobStorage:S3:Bucket is required.");
        }

        client = CreateClient(this.options.ServiceUrl);
        signer = string.IsNullOrEmpty(this.options.PublicServiceUrl) ? client : CreateClient(this.options.PublicServiceUrl);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = BlobKey.Validate(key),
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, cancellationToken);
    }

    public async Task<BlobDownload?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectAsync(options.Bucket, BlobKey.Validate(key), cancellationToken);
            return new BlobDownload(response.ResponseStream, response.Headers.ContentType ?? "application/octet-stream", response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await client.DeleteObjectAsync(options.Bucket, BlobKey.Validate(key), cancellationToken);

    public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) =>
        PresignAsync(key, HttpVerb.GET, contentType: null, lifetime);

    public Task<Uri> GetUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) =>
        PresignAsync(key, HttpVerb.PUT, contentType, lifetime);

    public async Task EnsureBucketAsync()
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(client, options.Bucket))
        {
            await client.PutBucketAsync(options.Bucket);
        }

        // Browsers upload and download straight from the bucket, so it has to allow cross-origin requests.
        // Any origin is fine for an emulator; a real bucket lists the app's origins.
        await client.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
        {
            BucketName = options.Bucket,
            Configuration = new CORSConfiguration
            {
                Rules = [new CORSRule { AllowedMethods = ["GET", "PUT"], AllowedOrigins = ["*"], AllowedHeaders = ["*"], MaxAgeSeconds = 3600 }],
            },
        });
    }

    public void Dispose()
    {
        client.Dispose();
        if (!ReferenceEquals(signer, client))
        {
            signer.Dispose();
        }
    }

    private async Task<Uri> PresignAsync(string key, HttpVerb verb, string? contentType, TimeSpan lifetime)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket,
            Key = BlobKey.Validate(key),
            Verb = verb,
            Expires = DateTime.UtcNow.Add(lifetime),
            // Presigned URLs default to https; local emulators usually serve plain http
            Protocol = (options.PublicServiceUrl ?? options.ServiceUrl)?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true
                ? Protocol.HTTP
                : Protocol.HTTPS,
        };
        if (contentType is not null)
        {
            // Signed in, so the upload must send exactly this Content-Type
            request.ContentType = contentType;
        }
        return new Uri(await signer.GetPreSignedURLAsync(request));
    }

    private AmazonS3Client CreateClient(string? serviceUrl)
    {
        var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        if (string.IsNullOrEmpty(serviceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            config.ServiceURL = serviceUrl;
            config.AuthenticationRegion = options.Region;
        }

        return string.IsNullOrEmpty(options.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }
}
