using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using FeedFabric.Application.Services.Storage;

namespace FeedFabric.Application.Services.Embeddings;

// Turns text into a 384-number vector where similar meanings are close together ("first parkrun" near "couch to
// 5k"), with all-MiniLM-L6-v2 from models/embeddings/minilm/ (ml/fetch_models.py). Mean pooling over the tokens,
// then scaled to length 1, so the dot product of two vectors is their cosine similarity.
public sealed class TextEmbedder(IBlobStorage storage) : IDisposable
{
    public const int Dimensions = 384;
    private const int MaxTokens = 128;
    // Where ml/fetch_models.py publishes it in blob storage
    public const string Prefix = "models/embeddings/minilm";

    private readonly SemaphoreSlim gate = new(1, 1);
    private InferenceSession? session;
    private BertTokenizer? tokenizer;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LoadAsync(cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var (model, words) = await LoadAsync(cancellationToken);
        if (texts.Count == 0)
        {
            return [];
        }

        var ids = texts.Select(t => words.EncodeToIds(t, MaxTokens, true, out _, out _)).ToList();
        var length = ids.Max(i => i.Count);
        var inputIds = new DenseTensor<long>([texts.Count, length]);
        var mask = new DenseTensor<long>([texts.Count, length]);
        var types = new DenseTensor<long>([texts.Count, length]);
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = 0; j < ids[i].Count; j++)
            {
                inputIds[i, j] = ids[i][j];
                mask[i, j] = 1;
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        };
        if (model.InputMetadata.ContainsKey("token_type_ids"))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", types));
        }

        using var results = model.Run(inputs);
        // [texts, tokens, Dimensions], read as one flat span: the tensor's indexer works out an offset per number
        var output = results.First().AsTensor<float>();
        var hidden = (output as DenseTensor<float> ?? output.ToDenseTensor()).Buffer.Span;
        var tokens = output.Dimensions[1];
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            var vector = new float[Dimensions];
            var sum = vector.AsSpan();
            for (var j = 0; j < ids[i].Count; j++)
            {
                var token = hidden.Slice((i * tokens + j) * Dimensions, Dimensions);
                for (var d = 0; d < Dimensions; d++)
                {
                    sum[d] += token[d];
                }
            }
            vectors[i] = Normalise(vector, ids[i].Count);
        }
        return vectors;
    }

    // Several floats at a time with SIMD: For You compares the viewer with every post in the last day
    public static float Cosine(float[] a, float[] b)
    {
        var x = a.AsSpan();
        var y = b.AsSpan(0, x.Length);
        var width = Vector<float>.Count;
        var sums = Vector<float>.Zero;
        var i = 0;
        for (; i <= x.Length - width; i += width)
        {
            sums += new Vector<float>(x[i..]) * new Vector<float>(y[i..]);
        }
        var sum = Vector.Sum(sums);
        for (; i < x.Length; i++)
        {
            sum += x[i] * y[i];
        }
        return sum;
    }

    // The average of several vectors, scaled to length 1: what someone's recent likes are about
    public static float[]? Mean(IEnumerable<float[]> vectors)
    {
        float[]? sum = null;
        var count = 0;
        foreach (var v in vectors)
        {
            sum ??= new float[v.Length];
            for (var i = 0; i < v.Length; i++)
            {
                sum[i] += v[i];
            }
            count++;
        }
        return sum is null ? null : Normalise(sum, count);
    }

    private static float[] Normalise(float[] vector, int count)
    {
        var length = 0f;
        for (var d = 0; d < vector.Length; d++)
        {
            vector[d] /= count;
            length += vector[d] * vector[d];
        }
        length = MathF.Sqrt(length);
        for (var d = 0; d < vector.Length; d++)
        {
            vector[d] /= length == 0 ? 1 : length;
        }
        return vector;
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
                await using var vocab = await storage.GetAsync($"{Prefix}/vocab.txt", cancellationToken)
                    ?? throw new ModelNotFoundException($"No embedding model at {Prefix}: run make models.");
                await using var modelFile = await storage.GetAsync($"{Prefix}/model.onnx", cancellationToken)
                    ?? throw new ModelNotFoundException($"No embedding model at {Prefix}: run make models.");
                using var vocabCopy = new MemoryStream();
                await vocab.Content.CopyToAsync(vocabCopy, cancellationToken);
                vocabCopy.Position = 0;
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
