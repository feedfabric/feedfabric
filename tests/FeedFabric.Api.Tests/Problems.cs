using System.Net.Http.Json;
using System.Text.Json;

namespace FeedFabric.Api.Tests;

public static class Problems
{
    // The Problem Details body's "code"
    public static async Task<string?> CodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
