using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Tests.Storage;

public class ModelFilesTests
{
    private sealed class MemoryStorage : IBlobStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Task<BlobDownload?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Files.TryGetValue(key, out var data) ? new BlobDownload(new MemoryStream(data), "application/octet-stream", data.Length) : null);

        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> GetUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task A_file_matching_its_checksum_reads_and_one_that_doesnt_reads_as_missing()
    {
        var storage = new MemoryStorage();
        var data = "model bytes"u8.ToArray();
        storage.Files["models/x/model.onnx"] = data;
        var sum = Convert.ToHexStringLower(SHA256.HashData(data));
        var cancellationToken = TestContext.Current.CancellationToken;

        using var read = await ModelFiles.ReadAsync(storage, "models/x/model.onnx", sum, "test model", cancellationToken);
        Assert.Equal(data, read.ToArray());

        storage.Files["models/x/model.onnx"] = "half a model"u8.ToArray();
        await Assert.ThrowsAsync<ModelNotFoundException>(() => ModelFiles.ReadAsync(storage, "models/x/model.onnx", sum, "test model", cancellationToken));
    }

    [Theory]
    [InlineData("synthetic", false, false)]
    [InlineData("all", false, false)]
    [InlineData("synthetic", true, true)]
    public async Task A_ranker_trained_on_simulated_behaviour_is_only_tried_where_allowed(string source, bool allow, bool tried)
    {
        var storage = new MemoryStorage();
        var latest = $$"""
            {"version":"v1","heads":{"open":"models/ranker/v1-open.onnx"},"features":{{System.Text.Json.JsonSerializer.Serialize(RankingFeatures.Names)}},"source":"{{source}}"}
            """;
        storage.Files[RankerModel.LatestKey] = Encoding.UTF8.GetBytes(latest);
        var logger = new CapturingLogger();
        using var ranker = new RankerModel(storage, Options.Create(new RankingOptions { AllowSimulatedRanker = allow }), TimeProvider.System, logger);

        Assert.False(await ranker.EnsureLoadedAsync(TestContext.Current.CancellationToken));

        // Allowed, it goes on to look for the head (missing here); refused, it never does
        Assert.Equal(tried, logger.Messages.Any(m => m.Contains("can't be used yet")));
        Assert.Equal(!tried, logger.Messages.Any(m => m.Contains("not members'")));
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<RankerModel>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
