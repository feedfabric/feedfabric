using System.Web;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Tests.Storage;

public sealed class LocalBlobStorageTests : BlobStorageContract, IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"feedfabric-blobs-{Guid.NewGuid():N}");
    private readonly ManualClock clock = new(DateTimeOffset.Parse("2026-11-05T12:00:00Z"));

    protected override Task<IBlobStorage> CreateAsync() => Task.FromResult<IBlobStorage>(CreateLocal());

    [Fact]
    public async Task Upload_url_verifies_for_its_own_key_content_type_and_operation_only()
    {
        var storage = CreateLocal();
        var (key, expires, sig, contentType) = Parse(await storage.GetUploadUrlAsync("tests/a.jpg", "image/jpeg", TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken));

        Assert.True(storage.Verify(LocalBlobOperation.Upload, key, contentType, expires, sig));
        Assert.False(storage.Verify(LocalBlobOperation.Upload, "tests/b.jpg", contentType, expires, sig));
        Assert.False(storage.Verify(LocalBlobOperation.Upload, key, "text/html", expires, sig));
        Assert.False(storage.Verify(LocalBlobOperation.Read, key, "", expires, sig));
        Assert.False(storage.Verify(LocalBlobOperation.Upload, key, contentType, expires + 3600, sig));
    }

    [Fact]
    public async Task Signed_urls_stop_working_when_they_expire()
    {
        var storage = CreateLocal();
        var (key, expires, sig, _) = Parse(await storage.GetReadUrlAsync("tests/a.jpg", TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken));

        Assert.True(storage.Verify(LocalBlobOperation.Read, key, "", expires, sig));
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(storage.Verify(LocalBlobOperation.Read, key, "", expires, sig));
    }

    [Fact]
    public void Refuses_to_start_without_a_long_signing_key()
    {
        Assert.Throws<InvalidOperationException>(() => new LocalBlobStorage(Options.Create(new BlobStorageOptions
        {
            Local = new LocalBlobStorageOptions { Root = root, SigningKey = "short" },
        }), clock));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalBlobStorage CreateLocal() => new(Options.Create(new BlobStorageOptions
    {
        Local = new LocalBlobStorageOptions
        {
            Root = root,
            PublicBaseUrl = "http://localhost:5161",
            SigningKey = "test-signing-key-0123456789-0123456789",
        },
    }), clock);

    private static (string Key, long Expires, string Sig, string ContentType) Parse(Uri url)
    {
        var query = HttpUtility.ParseQueryString(url.Query);
        return (url.AbsolutePath["/blobs/".Length..], long.Parse(query["expires"]!), query["sig"]!, query["contentType"] ?? "");
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
