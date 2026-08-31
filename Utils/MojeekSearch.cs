using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к веб-поиску Mojeek без API-ключа.
/// </summary>
public static class MojeekSearch
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

        // Безопасный поиск включён, чтобы уменьшить количество материалов для взрослых и рекламных результатов.
        var url = $"https://www.mojeek.com/search?q={Uri.EscapeDataString(query)}&safe=1";

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?>
            {
                ["Accept-Language"] = "en-US,en;q=0.8,ru;q=0.6",
                ["Referer"] = "https://www.mojeek.com/"
            },
            cancellationToken);

        if (LooksLikeChallenge(page))
        {
            Console.Error.WriteLine("[WebSearch] Mojeek: challenge/blocked page detected.");
            return result;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(page);

        // Текущая структура Mojeek: ul.results-standard > li, URL результата находится в один.ob.
        // Заголовок находится в h2 > один, а фрагмент — в p.s. Оставлены резервные селекторы на случай изменения HTML.
        var nodes = doc.DocumentNode.SelectNodes(
            "//ul[contains(concat(' ', normalize-space(@class), ' '), ' results-standard ')]/li" +
            "|//li[contains(concat(' ', normalize-space(@class), ' '), ' result ')]" +
            "|//div[contains(concat(' ', normalize-space(@class), ' '), ' result ')]" +
            "|//li[contains(@class,'result')]" );

        if (nodes == null || nodes.Count == 0)
        {
            Console.Error.WriteLine("[WebSearch] Mojeek: no result nodes matched.");
            return result;
        }

        foreach (var node in nodes)
        {
            if (result.Count >= top)
                break;

            var linkNode =
                node.SelectSingleNode(".//a[contains(concat(' ', normalize-space(@class), ' '), ' ob ')]") ??
                node.SelectSingleNode(".//h2//a[@href]") ??
                node.SelectSingleNode(".//a[@href]");

            if (linkNode == null)
                continue;

            var href = WebUtility.HtmlDecode(
                linkNode.GetAttributeValue("href", string.Empty)).Trim();

            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                uri.Host.Contains("mojeek.com", StringComparison.OrdinalIgnoreCase))
                continue;

            var titleNode =
                node.SelectSingleNode(".//h2//a") ??
                node.SelectSingleNode(".//h2") ??
                linkNode;

            var title = CleanNodeText(titleNode);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var snippetNode =
                node.SelectSingleNode(".//p[contains(concat(' ', normalize-space(@class), ' '), ' s ')]") ??
                node.SelectSingleNode(".//p[contains(@class,'snippet')]");

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

        Console.Error.WriteLine($"[WebSearch] Mojeek parser matched {result.Count} result(s).");
        return result;
    }

    private static bool LooksLikeChallenge(string html)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(html ?? string.Empty), @"\s+", " ").ToLowerInvariant();
        return text.Contains("captcha") ||
               text.Contains("verify you are human") ||
               text.Contains("access denied") ||
               text.Contains("checking your browser");
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
