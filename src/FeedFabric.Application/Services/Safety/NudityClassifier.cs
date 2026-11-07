using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using FeedFabric.Application.Services.Storage;
using SkiaSharp;

namespace FeedFabric.Application.Services.Safety;

// Scores how likely a photo is to contain nudity, with the ViT classifier ml/fetch_models.py publishes to
// models/safety/nsfw/ (ONNX, 8-bit, CPU). It runs on the rebuilt image, after the malware scan.
// Without the model it throws rather than guessing: a photo that couldn't be checked is never published.
public sealed class NudityClassifier(IBlobStorage storage) : IDisposable
{
    // Where ml/fetch_models.py publishes it in blob storage
    public const string Prefix = "models/safety/nsfw";
    private readonly SemaphoreSlim gate = new(1, 1);
    private InferenceSession? session;
    private Meta? meta;

    public async Task<double> ScoreAsync(byte[] image, CancellationToken cancellationToken)
    {
        var (model, settings) = await LoadAsync(cancellationToken);
        using var bitmap = SKBitmap.Decode(image) ?? throw new InvalidOperationException("The rebuilt image couldn't be decoded.");
        var size = settings.ImageSize;
        using var resized = bitmap.Resize(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear));

        // NCHW, each channel rescaled to 0..1 then normalised: (x - mean) / std, as the model was trained
        // Read straight from the RGBA pixel buffer rather than a native GetPixel call for each pixel
        var pixels = resized.GetPixelSpan();
        var rowBytes = resized.RowBytes;
        var plane = size * size;
        var values = new float[3 * plane];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var pixel = y * rowBytes + x * 4;
                var i = y * size + x;
                values[i] = (pixels[pixel] / 255f - settings.Mean[0]) / settings.Std[0];
                values[plane + i] = (pixels[pixel + 1] / 255f - settings.Mean[1]) / settings.Std[1];
                values[2 * plane + i] = (pixels[pixel + 2] / 255f - settings.Mean[2]) / settings.Std[2];
            }
        }
        var input = new DenseTensor<float>(values, [1, 3, size, size]);

        using var results = model.Run([NamedOnnxValue.CreateFromTensor(model.InputMetadata.Keys.First(), input)]);
        var logits = results.First().AsTensor<float>();
        var nsfw = Array.IndexOf(settings.Labels, "nsfw");
        // Softmax over the two labels
        var max = Math.Max(logits[0, 0], logits[0, 1]);
        var e0 = Math.Exp(logits[0, 0] - max);
        var e1 = Math.Exp(logits[0, 1] - max);
        return (nsfw == 0 ? e0 : e1) / (e0 + e1);
    }

    private async Task<(InferenceSession, Meta)> LoadAsync(CancellationToken cancellationToken)
    {
        if (session is not null && meta is not null)
        {
            return (session, meta);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session is null || meta is null)
            {
                await using var metaFile = await storage.GetAsync($"{Prefix}/meta.json", cancellationToken)
                    ?? throw new ModelNotFoundException($"No nudity model at {Prefix}: run make models.");
                var loaded = await JsonSerializer.DeserializeAsync<Meta>(metaFile.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
                    ?? throw new InvalidOperationException("The nudity model's meta.json is empty.");
                await using var modelFile = await storage.GetAsync($"{Prefix}/model.onnx", cancellationToken)
                    ?? throw new ModelNotFoundException($"No nudity model at {Prefix}: run make models.");
                using var bytes = new MemoryStream();
                await modelFile.Content.CopyToAsync(bytes, cancellationToken);
                session = new InferenceSession(bytes.ToArray());
                meta = loaded;
            }
            return (session, meta);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        session?.Dispose();
        gate.Dispose();
    }

    private sealed class Meta
    {
        public int ImageSize { get; set; } = 224;

        public float[] Mean { get; set; } = [0.5f, 0.5f, 0.5f];

        public float[] Std { get; set; } = [0.5f, 0.5f, 0.5f];

        public string[] Labels { get; set; } = ["normal", "nsfw"];
    }
}
