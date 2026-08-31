using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к веб-поиску Ecosia без API-ключа через извлечение данных из открытой
/// HTML-страница результатов. У Ecosia нет документированного JSON API без ключа, в отличие от Marginalia и
/// Baidu, поэтому используется извлечение данных из HTML, как у Mojeek и Yandex, с теми же ограничениями: структура
/// структура может измениться; несколько резервных селекторов используются для повышения устойчивости. Реализация
/// написана без возможности проверить актуальную страницу результатов Ecosia в реальном времени —
/// проверьте диагностику: отсутствие узлов результатов означает, что селекторы нужно обновить, а challenge
/// в диагностике указывает на защиту от ботов, а не на проблему селекторов.
/// блокирующий запрос.
/// </summary>
public static class EcosiaSearch
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

        var url = $"https://www.ecosia.org/search?q={Uri.EscapeDataString(query)}";

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?>
            {
                ["Accept-Language"] = "en-US,en;q=0.8",
                ["Referer"] = "https://www.ecosia.org/"
            },
            cancellationToken);

        if (LooksLikeChallenge(page))
        {
            Console.Error.WriteLine("[WebSearch] Ecosia: challenge/blocked page detected.");
            return result;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(page);

        // Ecosia's результаты имеют используемый один "mainline"/"результат" card structure с один
        // Шаблон result-title-anchor использовался исторически; предусмотрены резервные варианты на случай изменения
        // Разметка или имена классов могли измениться.
        var nodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'mainline-results')]//div[contains(@class,'result')]" +
            "|//div[contains(@class,'result-web')]" +
            "|//div[contains(@class,'web-result')]" +
            "|//article[contains(@class,'result')]" +
            "|//div[@data-test-id='mainline-result-web']" +
            "|//div[contains(@class,'result') and .//a[contains(@class,'result-title')]]");

        if (nodes == null || nodes.Count == 0)
        {
            Console.Error.WriteLine("[WebSearch] Ecosia: no result nodes matched.");
            return result;
        }

        foreach (var node in nodes)
        {
            if (result.Count >= top)
                break;

            var linkNode =
                node.SelectSingleNode(".//a[contains(@class,'result-title')]") ??
                node.SelectSingleNode(".//a[contains(@class,'result__link')]") ??
                node.SelectSingleNode(".//h2//a[@href]") ??
                node.SelectSingleNode(".//a[@href]");

            if (linkNode == null)
                continue;

            var href = WebUtility.HtmlDecode(linkNode.GetAttributeValue("href", string.Empty)).Trim();

            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                uri.Host.Contains("ecosia.org", StringComparison.OrdinalIgnoreCase))
                continue;

            var titleNode =
                node.SelectSingleNode(".//h2") ??
                linkNode;

            var title = CleanNodeText(titleNode);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var snippetNode =
                node.SelectSingleNode(".//p[contains(@class,'result-snippet')]") ??
                node.SelectSingleNode(".//p[contains(@class,'snippet')]") ??
                node.SelectSingleNode(".//p");

            var content = CleanNodeText(snippetNode ?? node);
            if (content.Length > 700)
                content = content[..700];

            var normalized = uri.ToString().TrimEnd('/');
            if (result.Any(x => string.Equals(x.Link, normalized, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new SearchResultItem
            {
                Title = title,
                Link = normalized,
                Content = content
            });
        }

        Console.Error.WriteLine($"[WebSearch] Ecosia parser matched {result.Count} result(s).");
        return result;
    }

    private static bool LooksLikeChallenge(string html)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(html ?? string.Empty), @"\s+", " ").ToLowerInvariant();
        return text.Contains("captcha") ||
               text.Contains("verify you are human") ||
               text.Contains("access denied") ||
               text.Contains("checking your browser") ||
               text.Contains("unusual traffic");
    }

    private static string CleanNodeText(HtmlNode node)
    {
        var parts = node.DescendantsAndSelf()
            .Where(n => n.NodeType == HtmlNodeType.Text)
            .Select(n => WebUtility.HtmlDecode(n.InnerText).Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return CleanText(string.Join(" ", parts));
    }

    private static string CleanText(string value) =>
        Regex.Replace(WebUtility.HtmlDecode(value ?? string.Empty), @"\s+", " ").Trim();
}
