using System.Text.Json;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к поиску Marginalia через открытый JSON API.
/// Регистрация не требуется: ключ "общий" документирован на
/// Документированный ключ "public" доступен без регистрации и описан на указанной странице.
/// Описывает назначение элемента.
/// При превышении общего лимита сервис может вернуть 503. Укажите WEB_SEARCH_MARGINALIA_API_KEY для
/// использования персонального ключа вместо общего.
/// для один private частота лимит.
/// </summary>
public static class MarginaliaSearch
{
    private const string DefaultApiKey = "public";

    public static async Task<List<SearchResultItem>> LoadAsync(
        string query,
        string? apiKey = null,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SearchResultItem>();
        if (string.IsNullOrWhiteSpace(query))
            return result;

        top = Math.Clamp(top, 1, 100);

        var key = string.IsNullOrWhiteSpace(apiKey) ? DefaultApiKey : apiKey;
        var url = $"https://api2.marginalia-search.com/search?query={Uri.EscapeDataString(query)}&count={top}";

        // Примечание: WebPageLoader больше не выбрасывает исключение для статусов, отличных от 2xx; см. WebPageLoader.ReadBodyAsync.
        // Статус записывается в stderr, тело возвращается вызывающему коду и затем разбирается как JSON.
        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?>
            {
                ["API-Key"] = key,
                ["Accept"] = "application/json"
            },
            cancellationToken);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(page);
        }
        catch (JsonException)
        {
            // Общий ключ часто получает ответ 503; непохожее на JSON тело означает, что этот
            // запрос didn't succeed. Treat as "движок unдоступный для этого запрос"
            // запрос не выполнен. Такая ошибка считается недоступностью движка и не останавливает агрегацию.
            Console.Error.WriteLine(
                "[WebSearch] Marginalia: response was not valid JSON (likely 503 rate limit on the shared public key).");
            return result;
        }

        using var _ = document;

        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            Console.Error.WriteLine("[WebSearch] Marginalia: response had no 'results' array.");
            return result;
        }

        foreach (var item in results.EnumerateArray())
        {
            if (result.Count >= top)
                break;

            var title = GetString(item, "title");
            var link = GetString(item, "url");
            var content = GetString(item, "description");

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

    private static string? GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
