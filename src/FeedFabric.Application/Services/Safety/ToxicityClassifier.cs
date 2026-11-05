using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Services.Safety;

// Scores text on six kinds of offence (toxic, severe_toxic, obscene, threat, insult, identity_hate), each 0 to 1 on
// its own, with unitary/toxic-bert from models/safety/toxicity/ (ml/fetch_models.py; ONNX, 8-bit, CPU). It reads
// meaning rather than words, so it catches abuse a word list misses: misspellings, "y0u", insults with no slur.
public sealed class ToxicityClassifier(IBlobStorage storage) : IDisposable
{
    private const string Prefix = "models/safety/toxicity";
    private readonly SemaphoreSlim gate = new(1, 1);
    private InferenceSession? session;
    private BertTokenizer? tokenizer;
    private string[] labels = [];
    private int maxTokens = 256;

    public async Task<IReadOnlyDictionary<string, double>> ScoreAsync(string text, CancellationToken cancellationToken)
    {
        var (model, words) = await LoadAsync(cancellationToken);
        var ids = words.EncodeToIds(text, maxTokens, true, out _, out _);
        var inputIds = new DenseTensor<long>([1, ids.Count]);
        var mask = new DenseTensor<long>([1, ids.Count]);
        for (var i = 0; i < ids.Count; i++)
        {
            inputIds[0, i] = ids[i];
            mask[0, i] = 1;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        };
        if (model.InputMetadata.ContainsKey("token_type_ids"))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>([1, ids.Count])));
        }

        using var results = model.Run(inputs);
        var logits = results.First().AsTensor<float>();
        // Each label on its own: a sigmoid, not a softmax
        return labels.Select((label, i) => (label, 1 / (1 + Math.Exp(-logits[0, i])))).ToDictionary(x => x.label, x => x.Item2);
    }

    private async Task<(InferenceSession, BertTokenizer)> LoadAsync(CancellationToken cancellationToken)
    {
        if (session is not null && tokenizer is not null)
        {
            return (session, tokenizer);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session is null || tokenizer is null)
            {
                await using var metaFile = await storage.GetAsync($"{Prefix}/meta.json", cancellationToken)
                    ?? throw new InvalidOperationException($"No toxicity model at {Prefix}: run make models.");
                using var meta = await JsonDocument.ParseAsync(metaFile.Content, cancellationToken: cancellationToken);
                labels = meta.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToArray();
                maxTokens = meta.RootElement.GetProperty("maxTokens").GetInt32();

                await using var vocab = await storage.GetAsync($"{Prefix}/vocab.txt", cancellationToken)
                    ?? throw new InvalidOperationException($"No toxicity model at {Prefix}: run make models.");
                using var vocabCopy = new MemoryStream();
                await vocab.Content.CopyToAsync(vocabCopy, cancellationToken);
                vocabCopy.Position = 0;
                await using var modelFile = await storage.GetAsync($"{Prefix}/model.onnx", cancellationToken)
                    ?? throw new InvalidOperationException($"No toxicity model at {Prefix}: run make models.");
                using var bytes = new MemoryStream();
                await modelFile.Content.CopyToAsync(bytes, cancellationToken);
                tokenizer = BertTokenizer.Create(vocabCopy, new BertOptions { LowerCaseBeforeTokenization = true });
                session = new InferenceSession(bytes.ToArray());
            }
            return (session, tokenizer);
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
}
