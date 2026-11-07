using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Services.Ranking;

// The trained ranker, from blob storage: ml/train_ranker.py writes models/ranker/<version>.onnx and points
// models/ranker/latest.json at it. Checked for a new version at most once a minute; until there is one (or if it
// was trained on different signals) the feed uses HeuristicRanker. ONNX Runtime runs on CPU, the same on a Mac
// and on a Linux server.
public sealed class RankerModel(IBlobStorage storage, IOptions<RankingOptions> options, TimeProvider timeProvider, ILogger<RankerModel> logger)
    : IDisposable
{
    public const string LatestKey = "models/ranker/latest.json";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim gate = new(1, 1);
    // One session per prediction (open, like, comment, long read, skip), or one "engaged" for older single models
    private Dictionary<string, InferenceSession> sessions = [];
    private DateTimeOffset nextCheck = DateTimeOffset.MinValue;

    public RankerModelInfo? Info { get; private set; }

    // The loaded model, after checking for a newer one if it's time; null means use the heuristic
    public async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() < nextCheck)
        {
            return sessions.Count > 0;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (timeProvider.GetUtcNow() >= nextCheck)
            {
                nextCheck = timeProvider.GetUtcNow().Add(CheckEvery);
                await ReloadAsync(cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
        return sessions.Count > 0;
    }

    // The post's value for each row of features: the weighted sum of the model's predictions,
    // e.g. 0.5 × P(open) + 1 × P(like) + 3 × P(comment) + 2 × P(long read) − 1.5 × P(skip).
    // goals replaces the weights the model was trained with (a ranking profile's Goals); an older single-prediction
    // model has no heads to weight, so keeps its own.
    public double[] Score(IReadOnlyList<double[]> rows, IReadOnlyDictionary<string, double>? goals = null)
    {
        var current = sessions;
        var weights = goals is not null && !current.ContainsKey("engaged") ? goals : Info?.Weights ?? [];
        if (current.Count == 0)
        {
            throw new InvalidOperationException("No ranker model is loaded.");
        }

        var total = new double[rows.Count];
        foreach (var (head, session) in current)
        {
            var weight = weights.GetValueOrDefault(head, 1);
            var probabilities = Predict(session, rows);
            for (var i = 0; i < rows.Count; i++)
            {
                total[i] += weight * probabilities[i];
            }
        }
        return total;
    }

    private static double[] Predict(InferenceSession current, IReadOnlyList<double[]> rows)
    {
        var width = RankingFeatures.Names.Length;
        // Filled as one flat row-major array, which is what the tensor holds anyway
        var values = new float[rows.Count * width];
        for (var i = 0; i < rows.Count; i++)
        {
            for (var j = 0; j < width; j++)
            {
                values[i * width + j] = (float)rows[i][j];
            }
        }
        var input = new DenseTensor<float>(values, [rows.Count, width]);

        var inputName = current.InputMetadata.Keys.First();
        using var results = current.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
        // A LightGBM classifier exported without ZipMap: "probabilities" is [rows, 2], column 1 the positive class
        var output = results.First(r => r.Name == "probabilities").AsTensor<float>();
        var probabilities = (output as DenseTensor<float> ?? output.ToDenseTensor()).Buffer.Span;
        var scores = new double[rows.Count];
        for (var i = 0; i < scores.Length; i++)
        {
            scores[i] = probabilities[i * 2 + 1];
        }
        return scores;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var latest = await storage.GetAsync(LatestKey, cancellationToken);
            if (latest is null)
            {
                return;
            }

            var info = await JsonSerializer.DeserializeAsync<RankerModelInfo>(latest.Content, JsonOptions, cancellationToken);
            if (info is null || info.Version == Info?.Version)
            {
                return;
            }
            if (info.Source is not (null or "real") && !options.Value.AllowSimulatedRanker)
            {
                logger.LogWarning("Ranker model {Version} was trained on {Source} behaviour, not members'; keeping the {Current}. " +
                    "Train with --source real, or set Ranking:AllowSimulatedRanker outside production",
                    info.Version, info.Source, Info?.Version ?? "heuristic");
                return;
            }
            if (!info.Features.SequenceEqual(RankingFeatures.Names))
            {
                logger.LogWarning("Ranker model {Version} was trained on different signals; keeping the heuristic", info.Version);
                return;
            }

            var heads = info.Heads is { Count: > 0 } ? info.Heads : new Dictionary<string, string> { ["engaged"] = info.ModelKey! };
            var next = new Dictionary<string, InferenceSession>();
            foreach (var (head, key) in heads)
            {
                MemoryStream bytes;
                try
                {
                    bytes = await ModelFiles.ReadAsync(storage, key, info.Sha256?.GetValueOrDefault(key), "ranker head", cancellationToken);
                }
                catch (ModelNotFoundException ex)
                {
                    logger.LogWarning("Ranker model {Version} can't be used yet: {Reason}", info.Version, ex.Message);
                    foreach (var loaded in next.Values)
                    {
                        loaded.Dispose();
                    }
                    return;
                }
                using (bytes)
                {
                    next[head] = new InferenceSession(bytes.ToArray());
                }
            }

            var previous = sessions;
            sessions = next;
            Info = info;
            foreach (var old in previous.Values)
            {
                old.Dispose();
            }
            logger.LogInformation("Loaded ranker model {Version} (AUC {Auc})", info.Version, info.Metrics.GetValueOrDefault("auc"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or OnnxRuntimeException)
        {
            logger.LogWarning(ex, "Couldn't load the ranker model; keeping what was there");
        }
    }

    public void Dispose()
    {
        foreach (var session in sessions.Values)
        {
            session.Dispose();
        }
        gate.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

// models/ranker/latest.json, written by ml/train_ranker.py
public sealed class RankerModelInfo
{
    public required string Version { get; set; }

    // A single-prediction model; newer models list Heads instead
    public string? ModelKey { get; set; }

    // Each prediction's model: "open", "like", "comment", "long_dwell", "skip" → models/ranker/<version>-<head>.onnx
    public Dictionary<string, string>? Heads { get; set; }

    // What each prediction is worth when adding them up into one score; negative for things to avoid
    public Dictionary<string, double> Weights { get; set; } = [];

    public required string[] Features { get; set; }

    public DateTime TrainedAt { get; set; }

    public int TrainingRows { get; set; }

    public double PositiveRate { get; set; }

    // Whose behaviour it learned from: "real" members, "synthetic" (the simulator) or "all"
    public string? Source { get; set; }

    // Each head's file (by key) to its SHA-256, checked on load; missing on models published before checksums
    public Dictionary<string, string>? Sha256 { get; set; }

    // Per head ("auc_like"…), and for the combined score ("value_ndcg_at_10") against the formula ("heuristic_value_ndcg_at_10")
    public Dictionary<string, double> Metrics { get; set; } = [];

    // How much each signal contributes to the model's splits (LightGBM "gain"), normalised to sum to 1
    public Dictionary<string, double> Importance { get; set; } = [];
}
