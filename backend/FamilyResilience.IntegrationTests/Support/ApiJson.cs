using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Reads and writes API JSON the way the API does: camelCase, enums as snake-case strings.</summary>
internal static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(Options, cancellationToken)
        ?? throw new InvalidOperationException("The response body was empty.");

    /// <summary>The <c>code</c> extension of a ProblemDetails body, or null.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The keys of a ValidationProblemDetails body's <c>errors</c>.</summary>
    public static async Task<IReadOnlyList<string>> ErrorKeysAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToList();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(
        this HttpClient client, string url, object body, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(url, body, Options, cancellationToken);

    /// <summary>Posts <paramref name="json"/> exactly as written, for bodies a serializer would never produce.</summary>
    public static async Task<HttpResponseMessage> PostRawJsonAsync(
        this HttpClient client, string url, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(url, content, cancellationToken);
    }

    public static Task<HttpResponseMessage> PutJsonAsync(
        this HttpClient client, string url, object body, CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(url, body, Options, cancellationToken);

    public static Task<HttpResponseMessage> PatchJsonAsync(
        this HttpClient client, string url, object body, CancellationToken cancellationToken) =>
        client.PatchAsJsonAsync(url, body, Options, cancellationToken);

    /// <summary>Patches with <paramref name="json"/> exactly as written, so a test can send an explicit null.</summary>
    public static async Task<HttpResponseMessage> PatchRawJsonAsync(
        this HttpClient client, string url, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PatchAsync(url, content, cancellationToken);
    }

    /// <summary><paramref name="json"/> with its first <paramref name="original"/> replaced; fails the test when there is none.</summary>
    public static string ReplaceFirst(this string json, string original, string replacement)
    {
        var index = json.IndexOf(original, StringComparison.Ordinal);
        Assert.True(index >= 0, $"The JSON does not contain {original}.");
        return string.Concat(json.AsSpan(0, index), replacement, json.AsSpan(index + original.Length));
    }
}
