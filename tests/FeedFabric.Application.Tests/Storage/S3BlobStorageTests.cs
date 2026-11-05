using System.Net;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Tests.Storage;

// Runs against LocalStack: docker compose --profile s3 up -d localstack. Skipped when it isn't running.
// Set S3_TEST_SERVICE_URL to point somewhere else.
public sealed class S3BlobStorageTests : BlobStorageContract
{
    private static readonly string ServiceUrl = Environment.GetEnvironmentVariable("S3_TEST_SERVICE_URL") ?? "http://localhost:4566";
    private static readonly Lazy<Task<bool>> Available = new(IsReachableAsync);

    protected override async Task<IBlobStorage> CreateAsync()
    {
        Assert.SkipUnless(await Available.Value, $"No S3 service at {ServiceUrl}. Start LocalStack: docker compose --profile s3 up -d localstack");
        var storage = new S3BlobStorage(Options.Create(new BlobStorageOptions
        {
            Provider = "S3",
            S3 = new S3BlobStorageOptions
            {
                Bucket = "feedfabric-tests",
                ServiceUrl = ServiceUrl,
                AccessKey = "test",
                SecretKey = "test",
                ForcePathStyle = true,
            },
        }));
        await storage.EnsureBucketAsync();
        return storage;
    }

    [Fact]
    public async Task A_client_can_upload_with_the_presigned_url_and_download_with_the_read_url()
    {
        var storage = await CreateAsync();
        var key = NewKey();
        using var http = new HttpClient();

        var uploadUrl = await storage.GetUploadUrlAsync(key, "text/plain", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        var upload = await http.PutAsync(uploadUrl, Body("from the browser", "text/plain"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var readUrl = await storage.GetReadUrlAsync(key, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        Assert.Equal("from the browser", await http.GetStringAsync(readUrl, TestContext.Current.CancellationToken));
    }

    private static async Task<bool> IsReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await http.GetAsync(ServiceUrl);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
