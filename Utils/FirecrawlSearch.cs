using System.Text.Json;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к endpoint Firecrawl /v2/поиск. После запуска режима "Firecrawl без ключа"
/// 28 августа 2026 года этот endpoint можно вызывать без заголовка авторизации:
/// доступно 1000 бесплатных кредитов в месяц с ограничением частоты запросов и без регистрации. Передача API-ключа
/// необязательна и нужна только для повышения персонального лимита. Реализация выполнена прямым REST-вызовом
/// вместо SDK Firecrawl NuGet, поэтому режим без ключа работает независимо от того, поддерживает ли установленная
/// версия SDK этот режим.
/// </summary>
public static class FirecrawlSearch
{
    /// <summary>
    /// Выполняет поиск Firecrawl и преобразует возвращённые записи в общий формат результатов поиска.
    /// </summary>
    /// <param name="query">Поисковый запрос.</param>
    /// <param name="apiKey">
    /// Необязательный API-ключ Firecrawl. Если значение null или пустое, запрос отправляется без ключа
    /// (без заголовка авторизации) с использованием бесплатного уровня Firecrawl без регистрации.
    /// </param>
    /// <returns>Разобранные результаты поиска.</returns>
    public static async Task<List<SearchResultItem>> LoadAsync(string query, string? apiKey = null, int top = 10)
    {
        var result = new List<SearchResultItem>();
        if (string.IsNullOrWhiteSpace(query))
            return result;

        top = Math.Clamp(top, 1, 100);

        var headers = new Dictionary<string, string?> { ["Content-Type"] = "application/json" };
        if (!string.IsNullOrWhiteSpace(apiKey))
            headers["Authorization"] = $"Bearer {apiKey}";
        else
            Console.Error.WriteLine("[WebSearch] Firecrawl: no API key set, using keyless free tier (rate-limited).");

        var payload = JsonSerializer.Serialize(new { query, limit = top });

        var page = await WebPageLoader.PostJson(
            "https://api.firecrawl.dev/v2/search",
            TimeSpan.FromSeconds(30),
            payload,
            headers);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(page);
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("[WebSearch] Firecrawl: response was not valid JSON.");
            return result;
        }

        using var _ = document;
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
            Console.Error.WriteLine($"[WebSearch] Firecrawl: request failed: {error ?? "unknown error"}");
            return result;
        }

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("web", out var web) ||
            web.ValueKind != JsonValueKind.Array)
        {
            Console.Error.WriteLine("[WebSearch] Firecrawl: no web results in response.");
            return result;
        }

        foreach (var item in web.EnumerateArray())
        {
            var title = GetString(item, "title");
            var link = GetString(item, "url");
            var content = GetString(item, "description");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            result.Add(new SearchResultItem { Title = title, Link = link, Content = content });
        }

        return result;
    }

    private static string? GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

