using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Tests;

// The real models from ml/fetch_models.py, in the repo's .blobs folder. Tests that need them are skipped until
// make models has been run.
public static class Models
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.blobs"));

    public static IBlobStorage Storage() => new LocalBlobStorage(Options.Create(new BlobStorageOptions
    {
        Local = new LocalBlobStorageOptions { Root = Root, SigningKey = "test-signing-key-0123456789-0123456789" },
    }), TimeProvider.System);

    public static void SkipUnlessPresent(string prefix) =>
        Assert.SkipUnless(File.Exists(Path.Combine(Root, prefix, "model.onnx")), $"No model at .blobs/{prefix}: run make models");
}
