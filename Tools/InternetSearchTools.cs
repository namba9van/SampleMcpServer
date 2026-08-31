using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет MCP-инструмент для параллельного поиска через несколько веб-поисковиков.
/// </summary>
internal class InternetSearchTools
{
    private static readonly (string Name, bool DefaultEnabled)[] KnownEngines =
    [
        ("DuckDuckGo", true),
        ("Yandex", true),
        ("Baidu", true),
        ("Mojeek", true),
        ("Marginalia", false),
        ("Qwant", false),
        ("Ecosia", false),
        ("Sogou", false),
        ("SearXNG", false),
        ("Firecrawl", false),
    ];

    [McpServerTool]
    [Description(
        "Выполняет параллельный поиск через настроенные поисковые системы, нормализует и " +
        "ранжирует результаты, удаляет дубли URL и возвращает наиболее подходящие результаты. " +
        "Поддерживаются DuckDuckGo, Yandex, Baidu, Mojeek, Marginalia, Qwant, Ecosia, " +
        "Sogou, SearXNG и Firecrawl. API-ключ не обязателен; для Firecrawl и Marginalia " +
        "его можно указать для повышения персонального лимита запросов.")]
    public async Task<string> WebSearch(
        [Description("Поисковый запрос.")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Поисковый запрос не может быть пустым.";

        var engineNames = ResolveEnabledEngines();

        var firecrawlApiKey =
            Environment.GetEnvironmentVariable("WEB_SEARCH_FIRECRAWL_API_KEY")
            ?? Environment.GetEnvironmentVariable("WEB_SEARCH_FirecrawApiKey");

        var marginaliaApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_MARGINALIA_API_KEY");

        var duckduckgoRegion =
            Environment.GetEnvironmentVariable("WEB_SEARCH_DUCKDUCKGO_REGION")
            ?? Environment.GetEnvironmentVariable("WEB_SEARCH_duckduckgoRegion");

        var searxngUrl = Environment.GetEnvironmentVariable("WEB_SEARCH_SEARXNG_URL");

        var tasks = new List<Task<List<SearchResultItem>>>();

        foreach (var engineName in engineNames)
        {
            switch (engineName.Trim().ToLowerInvariant())
            {
                case "duckduckgo":
                case "ddg":
                    tasks.Add(SearchSafe("DuckDuckGo", () => DuckDuckGoSearch.LoadAsync(
                        query, duckduckgoRegion, cancellationToken: default)));
                    break;

                case "yandex":
                case "яндекс":
                    tasks.Add(SearchSafe("Yandex", () => YandexSearch.LoadAsync(query, 10)));
                    break;

                case "baidu":
                    tasks.Add(SearchSafe("Baidu", async () =>
                        (await BaiduSearch.LoadAsync(query, 10)).ToList()));
                    break;

                case "mojeek":
                    tasks.Add(SearchSafe("Mojeek", () => MojeekSearch.LoadAsync(query, 10)));
                    break;

                case "marginalia":
                    tasks.Add(SearchSafe("Marginalia", () =>
                        MarginaliaSearch.LoadAsync(query, marginaliaApiKey, 10)));
                    break;

                case "qwant":
                    tasks.Add(SearchSafe("Qwant", () => QwantSearch.LoadAsync(query, 10)));
                    break;

                case "ecosia":
                    tasks.Add(SearchSafe("Ecosia", () => EcosiaSearch.LoadAsync(query, 10)));
                    break;

                case "sogou":
                    tasks.Add(SearchSafe("Sogou", () => SogouSearch.LoadAsync(query, 10)));
                    break;

                case "searxng":
                case "searx":
                    if (!string.IsNullOrWhiteSpace(searxngUrl))
                    {
                        tasks.Add(SearchSafe("SearXNG", () =>
                            SearxngSearch.LoadAsync(query, searxngUrl, 10)));
                    }
                    else
                    {
                        Console.Error.WriteLine("[WebSearch] SearXNG skipped: WEB_SEARCH_SEARXNG_URL is not set.");
                    }
                    break;

                case "firecrawl":
                    // Без ключа: после запуска Firecrawl Keyless 28 августа 2026 года сервис работает без API-ключа.
                    // Ключ необязателен: бесплатный уровень имеет ограничение частоты запросов. Ключ позволяет повысить персональный лимит.
                    // личный частота лимит только.
                    tasks.Add(SearchSafe("Firecrawl", () =>
                        FirecrawlSearch.LoadAsync(query, firecrawlApiKey, 10)));
                    break;

                default:
                    Console.Error.WriteLine($"[WebSearch] Unknown engine skipped: {engineName}");
                    break;
            }
        }

        if (tasks.Count == 0)
            return "No usable search engines are configured. Set WEB_SEARCH_ENGINES or configure the required engine settings.";

        var batches = await Task.WhenAll(tasks);
        var candidates = batches.SelectMany(x => x)
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.Link))
            .ToList();

        var filtered = FilterUnsafeAndLowQuality(candidates, query);

        Console.Error.WriteLine(
            $"[WebSearch] Aggregated: {candidates.Count} candidate(s), {filtered.Count} after filtering.");

        var ranked = RankAndDeduplicate(filtered, query);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
        };

        return JsonSerializer.Serialize(ranked, options);
    }

    /// <summary>
    /// Определяет включённые поисковые системы.
    /// Если задан WEB_SEARCH_ENGINES, используется этот список.
    /// Иначе применяются отдельные WEB_SEARCH_ENABLE_* с их значениями по умолчанию.
    /// </summary>
    private static string[] ResolveEnabledEngines()
    {
        var legacyList = Environment.GetEnvironmentVariable("WEB_SEARCH_ENGINES");
        if (!string.IsNullOrWhiteSpace(legacyList))
        {
            return legacyList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var enabled = new List<string>();
        foreach (var (name, defaultEnabled) in KnownEngines)
        {
            var envVar = "WEB_SEARCH_ENABLE_" + name.ToUpperInvariant();
            var raw = Environment.GetEnvironmentVariable(envVar);

            var isEnabled = raw is null ? defaultEnabled : ParseBool(raw, defaultEnabled);

            if (isEnabled)
                enabled.Add(name);
            else
                Console.Error.WriteLine($"[WebSearch] {name} disabled via {envVar} (or its default).");
        }

        return enabled.ToArray();
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" or "enabled" => true,
            "0" or "false" or "no" or "off" or "disabled" => false,
            _ => fallback
        };
    }

    private static async Task<List<SearchResultItem>> SearchSafe(
        string engine,
        Func<Task<List<SearchResultItem>>> search)
    {
        try
        {
            var results = await search();

            foreach (var result in results)
                result.Engine ??= engine;

            Console.Error.WriteLine($"[WebSearch] {engine}: {results.Count} result(s).");
            if (results.Count == 0)
                Console.Error.WriteLine($"[WebSearch] {engine}: no usable results returned.");
            return results;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[WebSearch] {engine} failed: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    private static List<SearchResultItem> RankAndDeduplicate(
        IEnumerable<SearchResultItem> candidates,
        string query)
    {
        var queryTokens = Tokenize(query);

        // Нормализуем URL и рассчитываем релевантность перед удалением дублей.
        var normalized = candidates
            .Select(item =>
            {
                item.Link = NormalizeUrl(item.Link!);
                return item;
            })
            .Where(item => Uri.TryCreate(item.Link, UriKind.Absolute, out var uri) &&
                           uri.Scheme is "http" or "https")
            .Select(item => new ScoredResult(item, CalculateScore(item, queryTokens)))
            .ToList();

        var deduplicated = normalized
            .GroupBy(x => CanonicalUrl(x.Item.Link!), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Item.Content?.Length ?? 0)
                    .First();

                // Если другой движок вернул тот же результат с более полным текстом, сохраняем его содержимое.
                // Если у другого движка тот же результат содержит существенно больше текста, используется это содержимое.
                var richest = group.OrderByDescending(x => x.Item.Content?.Length ?? 0).First().Item;
                if ((richest.Content?.Length ?? 0) > (best.Item.Content?.Length ?? 0))
                    best.Item.Content = richest.Content;

                return best;
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Content?.Length ?? 0)
            .Take(50)
            .Select(x => x.Item)
            .ToList();

        return deduplicated;
    }

    private static List<SearchResultItem> FilterUnsafeAndLowQuality(
        IEnumerable<SearchResultItem> candidates,
        string query)
    {
        var queryTokens = Tokenize(query);
        var filtered = new List<SearchResultItem>();

        foreach (var item in candidates)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Link))
                continue;

            if (IsCaptchaOrBotChallenge(item))
            {
                Console.Error.WriteLine(
                    $"[WebSearch] Filtered CAPTCHA/bot challenge: {item.Engine ?? "unknown"} {item.Link}");
                continue;
            }

            if (IsBlockedDomain(item.Link))
            {
                Console.Error.WriteLine(
                    $"[WebSearch] Filtered blocked domain: {item.Engine ?? "unknown"} {item.Link}");
                continue;
            }

            var score = CalculateScore(item, queryTokens);

            // поиск движки может возврат unrelated страницы, especially для broad запросы.
            // Не пропускаем результат только потому, что его вернул один из поисковых движков.
            var hasQueryMatch = queryTokens.Count == 0 ||
                Tokenize(item.Title ?? string.Empty).Intersect(queryTokens).Any() ||
                Tokenize(item.Content ?? string.Empty).Intersect(queryTokens).Any();

            if (!hasQueryMatch)
            {
                Console.Error.WriteLine(
                    $"[WebSearch] Filtered irrelevant result: score={score} {item.Link}");
                continue;
            }

            if (score < MinimumScore(queryTokens.Count))
            {
                Console.Error.WriteLine(
                    $"[WebSearch] Filtered low-score result: score={score} {item.Link}");
                continue;
            }

            filtered.Add(item);
        }

        return filtered;
    }

    private static int MinimumScore(int queryTokenCount)
    {
        // Для однословного запроса требуется меньше подтверждений релевантности, чем для многословного.
        return queryTokenCount switch
        {
            <= 1 => 10,
            2 => 12,
            3 => 14,
            _ => 16
        };
    }

    private static int CalculateScore(SearchResultItem item, HashSet<string> queryTokens)
    {
        var score = 0;
        var title = item.Title ?? string.Empty;
        var content = item.Content ?? string.Empty;
        var link = item.Link ?? string.Empty;

        var titleTokens = Tokenize(title);
        var contentTokens = Tokenize(content);

        score += queryTokens.Count(token => titleTokens.Contains(token)) * 18;
        score += queryTokens.Count(token => contentTokens.Contains(token)) * 3;

        if (queryTokens.Count > 0 && queryTokens.All(titleTokens.Contains))
            score += 30;

        if (IsPreferredDomain(link))
            score += 35;

        if (IsLowQualityDomain(link))
            score -= 25;

        if (LooksLikeSpam(title, content))
            score -= 100;

        if (LooksAdult(title, content, link))
            score -= 200;

        // Предпочитаем полезный фрагмент, но не позволяем слишком длинному тексту определять релевантность.
        score += Math.Min(content.Length / 120, 8);

        if (LooksRussian(item) && queryTokens.Any(IsCyrillic))
            score += 12;
        else if (!LooksRussian(item) && queryTokens.Any(IsCyrillic))
            score -= 4;

        return score;
    }

    private static bool IsCaptchaOrBotChallenge(SearchResultItem item)
    {
        var text = $"{item.Title} {item.Content} {item.Link}".ToLowerInvariant();

        string[] markers =
        [
            "smartcaptcha",
            "smart captcha",
            "captcha",
            "i'm not a robot",
            "im not a robot",
            "i am not a robot",
            "я не робот",
            "проверить, что вы не робот",
            "проверить что вы не робот",
            "подтвердите, что вы не робот",
            "подтвердите что вы не робот",
            "verify you are human",
            "verify you're human",
            "checking your browser",
            "checking your browser before accessing",
            "access denied",
            "cf-chl-",
            "/captcha"
        ];

        return markers.Any(text.Contains);
    }

    private static bool IsBlockedDomain(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return true;

        var host = uri.Host.ToLowerInvariant();

        // Известные домены с материалами для взрослых и спамом, которые нельзя возвращать в обычном веб-поиске.
        string[] blockedHosts =
        [
            "prostoporno.net"
        ];

        return blockedHosts.Any(blocked =>
            host == blocked || host.EndsWith("." + blocked, StringComparison.Ordinal));
    }

    private static bool LooksAdult(string title, string content, string link)
    {
        var text = $"{title} {content} {link}".ToLowerInvariant();

        string[] markers =
        [
            "porn", "porno", "порно", "порн", "xxx", "nsfw",
            "sex video", "sexvideos", "порновидео", "порнуха",
            "pornhub", "xvideos", "xnxx"
        ];

        return markers.Any(text.Contains);
    }

    private static bool IsPreferredDomain(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        return host == "github.com" || host.EndsWith(".github.com", StringComparison.Ordinal) ||
               host == "stackoverflow.com" || host.EndsWith(".stackoverflow.com", StringComparison.Ordinal) ||
               host == "stackexchange.com" || host.EndsWith(".stackexchange.com", StringComparison.Ordinal) ||
               host == "wikipedia.org" || host.EndsWith(".wikipedia.org", StringComparison.Ordinal) ||
               host == "microsoft.com" || host.EndsWith(".microsoft.com", StringComparison.Ordinal) ||
               host == "dotnet.microsoft.com" || host.EndsWith(".dotnet.microsoft.com", StringComparison.Ordinal) ||
               host.EndsWith(".gov", StringComparison.Ordinal) ||
               host.EndsWith(".gov.ru", StringComparison.Ordinal) ||
               host.EndsWith(".edu", StringComparison.Ordinal) ||
               host.EndsWith(".edu.ru", StringComparison.Ordinal);
    }

    private static bool IsLowQualityDomain(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        return host.Contains("pinterest.", StringComparison.Ordinal) ||
               host.Contains("slideshare.", StringComparison.Ordinal) ||
               host.Contains("scribd.", StringComparison.Ordinal) ||
               host.Contains("clickadu.", StringComparison.Ordinal) ||
               host.Contains("adsterra.", StringComparison.Ordinal);
    }

    private static bool LooksLikeSpam(string title, string content)
    {
        var text = $"{title} {content}".ToLowerInvariant();
        return text.Contains("купить дешево") ||
               text.Contains("casino") ||
               text.Contains("betting") ||
               text.Contains("viagra");
    }

    private static bool LooksRussian(SearchResultItem item)
    {
        var text = $"{item.Title} {item.Content}";
        var cyrillic = text.Count(ch => ch is >= 'А' and <= 'я' or >= 'Ё' and <= 'ё');
        var latin = text.Count(ch => (ch is >= 'A' and <= 'Z') || (ch is >= 'a' and <= 'z'));
        return cyrillic > latin;
    }

    private static bool IsCyrillic(string token) =>
        token.Any(ch => ch is >= 'а' and <= 'я' or >= 'ё' and <= 'ё');

    private static HashSet<string> Tokenize(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{Nd}]{2,}")
            .Select(m => m.Value)
            .Where(x => x.Length >= 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return url.Trim().TrimEnd('/');

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Host = uri.Host.ToLowerInvariant()
        };

        var queryParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var key = part.Split('=', 2)[0];
                return !(key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("gclid", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("fbclid", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("yclid", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("mc_cid", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("mc_eid", StringComparison.OrdinalIgnoreCase));
            });

        builder.Query = string.Join('&', queryParts);
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string CanonicalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.TrimEnd('/');

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];
        if (host.StartsWith("m.", StringComparison.Ordinal))
            host = host[2..];

        var builder = new UriBuilder(uri)
        {
            Host = host,
            Fragment = ""
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private sealed record ScoredResult(SearchResultItem Item, int Score);
}
