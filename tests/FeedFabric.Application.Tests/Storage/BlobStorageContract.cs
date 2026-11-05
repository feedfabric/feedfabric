using System.Text;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Tests.Storage;

// Behaviour every IBlobStorage must share, run once per provider
public abstract class BlobStorageContract
{
    protected abstract Task<IBlobStorage> CreateAsync();

    protected static string NewKey() => $"tests/{Guid.NewGuid():N}/file.txt";

    [Fact]
    public async Task Put_then_get_returns_the_same_bytes_and_content_type()
    {
        var storage = await CreateAsync();
        var key = NewKey();

        await storage.PutAsync(key, new MemoryStream("hello"u8.ToArray()), "text/plain", TestContext.Current.CancellationToken);
        await using var blob = await storage.GetAsync(key, TestContext.Current.CancellationToken);

        Assert.NotNull(blob);
        Assert.Equal("text/plain", blob.ContentType);
        Assert.Equal("hello", await new StreamReader(blob.Content).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Put_overwrites_an_existing_key()
    {
        var storage = await CreateAsync();
        var key = NewKey();

        await storage.PutAsync(key, new MemoryStream("first"u8.ToArray()), "text/plain", TestContext.Current.CancellationToken);
        await storage.PutAsync(key, new MemoryStream("second"u8.ToArray()), "text/plain", TestContext.Current.CancellationToken);
        await using var blob = await storage.GetAsync(key, TestContext.Current.CancellationToken);

        Assert.Equal("second", await new StreamReader(blob!.Content).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_returns_null_for_a_missing_key()
    {
        var storage = await CreateAsync();

        Assert.Null(await storage.GetAsync(NewKey(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_removes_the_blob_and_can_be_repeated()
    {
        var storage = await CreateAsync();
        var key = NewKey();
        await storage.PutAsync(key, new MemoryStream("x"u8.ToArray()), "text/plain", TestContext.Current.CancellationToken);

        await storage.DeleteAsync(key, TestContext.Current.CancellationToken);
        await storage.DeleteAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(await storage.GetAsync(key, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("tests/../../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("back\\slash.txt")]
    [InlineData("")]
    public async Task Rejects_keys_that_could_escape_the_bucket_or_folder(string key)
    {
        var storage = await CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.PutAsync(key, new MemoryStream("x"u8.ToArray()), "text/plain", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.GetUploadUrlAsync(key, "text/plain", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken));
    }

    protected static StringContent Body(string text, string contentType)
    {
        var content = new StringContent(text, Encoding.UTF8);
        content.Headers.ContentType = new(contentType);
        return content;
    }
}
