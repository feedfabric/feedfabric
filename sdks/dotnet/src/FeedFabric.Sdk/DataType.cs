using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FeedFabric.Sdk;

/// <summary>
/// One of your data types, typed. The API checks data against the type's JSON Schema; this makes the same promise in
/// your code, so a weight reading can't be posted as anything else and reading one back gives your own type.
/// </summary>
/// <example>
/// <code>
/// public record WeightReading(double Kg, string? Note = null);
/// static readonly DataType&lt;WeightReading&gt; Weight = new("weight-reading");
///
/// await member.Posts.CreateAsync(new PostInput { Tags = ["weight"], Data = Weight.Of(new WeightReading(82.4)) });
/// if (Weight.TryRead(post.Data, out var reading)) { ... }
/// </code>
/// </example>
/// <param name="key">The type's key, as registered in the admin panel</param>
/// <param name="options">How T is written and read; camelCase (JsonSerializerDefaults.Web) by default</param>
public sealed class DataType<T>(string key, JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions options = options ?? FeedFabricJson.Options;

    public string Key { get; } = key;

    /// <summary>The post's data, for creating or editing a post.</summary>
    public PostDataInput Of(T value) => new(Key, JsonSerializer.SerializeToElement(value, options));

    /// <summary>Whether a post's data is of this type.</summary>
    public bool Is([NotNullWhen(true)] PostData? data) => data is not null && data.Type == Key;

    /// <summary>The post's data as a T, if it has data of this type. Throws a JsonException if it does but the value isn't a T.</summary>
    public bool TryRead(PostData? data, [MaybeNullWhen(false)] out T value)
    {
        if (!Is(data))
        {
            value = default;
            return false;
        }
        value = data.Value.Deserialize<T>(options)!;
        return true;
    }

    /// <summary>The post's data as a T, or default if it has no data of this type.</summary>
    public T? Read(PostData? data) => TryRead(data, out var value) ? value : default;
}
