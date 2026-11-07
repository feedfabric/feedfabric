using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Services.Safety;

// Scores text on seven kinds of offence (toxicity, severe_toxicity, obscene, identity_attack, insult, threat,
// sexual_explicit), each 0 to 1 on its own, plus which identities it mentions, with Detoxify's unbiased model
// (unitary/unbiased-toxic-roberta) from models/safety/toxicity/: as published by make models, or fine-tuned on this
// app's wording by make tune-toxicity (ml/tune_toxicity.py) so "kill it at the race" isn't a threat. ONNX, 8-bit, CPU.
// It reads meaning rather than words, so it catches abuse a word list misses: misspellings, "y0u", insults with no slur.
public sealed class ToxicityClassifier(IBlobStorage storage) : IDisposable
{
    // Where ml/fetch_models.py publishes it in blob storage
    public const string Prefix = "models/safety/toxicity";
    private readonly SemaphoreSlim gate = new(1, 1);
    private InferenceSession? session;
    private CodeGenTokenizer? tokenizer;
    private int startId;
    private int endId;
    private string[] labels = [];
    private int maxTokens = 256;

    public async Task<IReadOnlyDictionary<string, double>> ScoreAsync(string text, CancellationToken cancellationToken)
    {
        var (model, words) = await LoadAsync(cancellationToken);
        // RoBERTa's byte-level BPE, the same as GPT-2's, wrapped in <s> … </s>
        var ids = new List<int>(maxTokens) { startId };
        ids.AddRange(words.EncodeToIds(text, maxTokens - 2, out _, out _));
        ids.Add(endId);
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

    private async Task<(InferenceSession, CodeGenTokenizer)> LoadAsync(CancellationToken cancellationToken)
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
                    ?? throw new ModelNotFoundException($"No toxicity model at {Prefix}: run make models.");
                using var meta = await JsonDocument.ParseAsync(metaFile.Content, cancellationToken: cancellationToken);
                labels = meta.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToArray();
                maxTokens = meta.RootElement.GetProperty("maxTokens").GetInt32();

                using var vocab = await ReadAsync("vocab.json", cancellationToken);
                using var merges = await ReadAsync("merges.txt", cancellationToken);
                using var bytes = await ReadAsync("model.onnx", cancellationToken);
                tokenizer = CodeGenTokenizer.Create(vocab, merges, addPrefixSpace: false, addBeginOfSentence: false, addEndOfSentence: false);
                startId = tokenizer.Vocabulary["<s>"];
                endId = tokenizer.Vocabulary["</s>"];
                session = new InferenceSession(bytes.ToArray());
            }
            return (session, tokenizer);
        }
        finally
        {
            gate.Release();
        }
    }

    // The whole file, since the tokenizer and ONNX Runtime both want a seekable stream or bytes
    private async Task<MemoryStream> ReadAsync(string name, CancellationToken cancellationToken)
    {
        await using var file = await storage.GetAsync($"{Prefix}/{name}", cancellationToken)
            ?? throw new ModelNotFoundException($"No {name} for the toxicity model at {Prefix}: run make models.");
        var copy = new MemoryStream();
        await file.Content.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }

    public void Dispose()
    {
        session?.Dispose();
        gate.Dispose();
    }
}
