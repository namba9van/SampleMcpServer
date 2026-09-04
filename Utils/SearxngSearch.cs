using System.Text.Json;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к экземпляру SearXNG. API-ключ не требуется, но нужен адрес
/// работающего экземпляра (см. параметр baseUrl / переменную WEB_SEARCH_SEARXNG_URL).
/// </summary>
public static class SearxngSearch
{
    public static async Task<List<SearchResultItem>> LoadAsync(
        string query,
        string baseUrl,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SearchResultItem>();

        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(baseUrl))
            return result;

        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("WEB_SEARCH_SEARXNG_URL must be a valid http(s) URL.");

        top = Math.Clamp(top, 1, 50);

        var url = $"{baseUri}search?q={Uri.EscapeDataString(query)}&format=json&language=all";

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?> { ["Accept"] = "application/json" },
            cancellationToken);

        using var document = JsonDocument.Parse(page);

        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in results.EnumerateArray())
        {
            if (result.Count >= top)
                break;

            var title = GetString(item, "title");
            var link = GetString(item, "url");
            var content = GetString(item, "content");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
                continue;

            if (result.Any(x => string.Equals(x.Link, uri.ToString(), StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new SearchResultItem
            {
                Title = title.Trim(),
                Link = uri.ToString(),
                Content = content?.Trim()
            });
        }

        return result;
    }

    private static string? GetString(JsonElement item, string property)
    {
        return item.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
