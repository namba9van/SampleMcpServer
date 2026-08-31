using System.Text.Json;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к недокументированному JSON API поиска Qwant v3. API-ключ не требуется,
/// однако перед этим endpoint используется защита от ботов Datadome. Реализация движка SearXNG
/// Описывает назначение элемента.
/// Описывает назначение элемента.
/// Описывает назначение элемента.
/// иногда корректно возвращает пустой результат с записью в журнал. Обход защиты не выполняется.
/// </summary>
public static class QwantSearch
{
    public static async Task<List<SearchResultItem>> LoadAsync(
        string query,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SearchResultItem>();
        if (string.IsNullOrWhiteSpace(query))
            return result;

        top = Math.Clamp(top, 1, 50);

        var url = "https://api.qwant.com/v3/search/web" +
                   $"?q={Uri.EscapeDataString(query)}" +
                   $"&count={top}" +
                   "&locale=en_US&offset=0&device=desktop&safesearch=1";

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?>
            {
                ["Accept"] = "application/json",
                ["Referer"] = "https://www.qwant.com/"
            },
            cancellationToken);

        if (LooksLikeChallenge(page))
        {
            Console.Error.WriteLine("[WebSearch] Qwant: Datadome challenge/rate-limit detected.");
            return result;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(page);
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("[WebSearch] Qwant: response was not valid JSON.");
            return result;
        }

        using var _ = document;
        var root = document.RootElement;

        if (root.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String &&
            !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            var errorCode = TryGetErrorCode(root);
            Console.Error.WriteLine(
                $"[WebSearch] Qwant: API returned status={status.GetString()}" +
                (errorCode is null ? "" : $", error code={errorCode}") +
                (errorCode == 24 ? " (rate limited / CAPTCHA wall)." : "."));
            return result;
        }

        // Qwant nests результаты as data.результат.элементы.mainline[].элементы[] (каждый mainline
        // Техническое примечание к реализации.
        // Техническое примечание к реализации.
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("result", out var resultEl) ||
            !resultEl.TryGetProperty("items", out var itemsEl) ||
            !itemsEl.TryGetProperty("mainline", out var mainline) ||
            mainline.ValueKind != JsonValueKind.Array)
        {
            Console.Error.WriteLine("[WebSearch] Qwant: unexpected response shape, no mainline items found.");
            return result;
        }

        foreach (var block in mainline.EnumerateArray())
        {
            if (result.Count >= top)
                break;

            var blockType = GetString(block, "type");
            if (!string.Equals(blockType, "web", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!block.TryGetProperty("items", out var entries) || entries.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var entry in entries.EnumerateArray())
            {
                if (result.Count >= top)
                    break;

                var title = GetString(entry, "title");
                var link = GetString(entry, "url");
                var content = GetString(entry, "desc");

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                    continue;

                if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    continue;

                if (result.Any(x => string.Equals(x.Link, uri.ToString(), StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Add(new SearchResultItem
                {
                    Title = CleanText(title),
                    Link = uri.ToString(),
                    Content = content is null ? null : CleanText(content)
                });
            }
        }

        Console.Error.WriteLine($"[WebSearch] Qwant parsed {result.Count} result(s).");
        return result;
    }

    private static int? TryGetErrorCode(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data) &&
            data.TryGetProperty("error_code", out var code) &&
            code.ValueKind == JsonValueKind.Number)
            return code.GetInt32();

        return null;
    }

    private static bool LooksLikeChallenge(string body)
    {
        var text = body.ToLowerInvariant();
        return text.Contains("datadome") ||
               text.Contains("captcha") ||
               text.Contains("geo.captcha-delivery.com");
    }

    private static string? GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string CleanText(string value) =>
        Regex.Replace(System.Net.WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
}
