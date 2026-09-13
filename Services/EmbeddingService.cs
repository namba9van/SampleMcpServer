using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Services;

/// <summary>
/// Generates embeddings using an OpenAI-compatible endpoint.
/// Falls back to a deterministic local hashing vector when no endpoint is configured.
/// </summary>
public sealed class EmbeddingService
{
    private const int FallbackDimensions = 256;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[FallbackDimensions];

        var endpoint = Environment.GetEnvironmentVariable("EMBEDD_ENDPOINT")?.Trim();
        var model = Environment.GetEnvironmentVariable("EMBEDD_MODEL")?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            return CreateFallback(text);

        var baseUri = endpoint.EndsWith('/') ? endpoint : endpoint + "/";
        var uri = new Uri(new Uri(baseUri), "embeddings");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        var apiKey = Environment.GetEnvironmentVariable("EMBEDD_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        request.Content = JsonContent.Create(new { model, input = text });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return CreateFallback(text);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var embedding = json.RootElement.GetProperty("data")[0].GetProperty("embedding");
        var result = new float[embedding.GetArrayLength()];
        var index = 0;
        foreach (var value in embedding.EnumerateArray())
            result[index++] = value.GetSingle();
        return result;
    }

    private static float[] CreateFallback(string text)
    {
        var vector = new float[FallbackDimensions];
        var tokens = text.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var slot = BitConverter.ToUInt32(hash, 0) % FallbackDimensions;
            vector[slot] += (hash[4] & 1) == 0 ? 1f : -1f;
        }

        var norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / norm);
        }
        return vector;
    }
}
