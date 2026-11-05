using System.Text.Json;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Tests;

// The real models from ml/fetch_models.py, in the repo's .blobs folder. Tests that need them are skipped until
// make models has been run. A model is there once its meta.json is: both ml scripts write it last.
public static class Models
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.blobs"));

    public static IBlobStorage Storage() => new LocalBlobStorage(Options.Create(new BlobStorageOptions
    {
        Local = new LocalBlobStorageOptions { Root = Root, SigningKey = "test-signing-key-0123456789-0123456789" },
    }), TimeProvider.System);

    public static void SkipUnlessPresent(string prefix) =>
        Assert.SkipUnless(File.Exists(Meta(prefix)), $"No model at .blobs/{prefix}: run make models");

    // For behaviour only ml/tune_toxicity.py's fine-tuning gives, which marks its meta.json with tunedAt
    public static void SkipUnlessTuned(string prefix)
    {
        SkipUnlessPresent(prefix);
        using var meta = JsonDocument.Parse(File.ReadAllBytes(Meta(prefix)));
        Assert.SkipUnless(meta.RootElement.TryGetProperty("tunedAt", out _), $"The model at .blobs/{prefix} isn't fine-tuned: run make tune-toxicity");
    }

    private static string Meta(string prefix) => Path.Combine(Root, prefix, "meta.json");
}
