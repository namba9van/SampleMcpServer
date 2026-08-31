using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к веб-поиску Sogou без API-ключа через извлечение данных из открытой HTML-страницы
/// результатов. В отличие от Baidu, для Sogou не найден бесплатный JSON endpoint без ключа, поэтому используется
/// извлечение из HTML, как у Ecosia, Mojeek и Yandex, с теми же ограничениями и несколькими
/// резервными селекторами. Реализация написана без возможности проверить актуальную страницу результатов в реальном времени.
/// Для сравнения можно было использовать So.com (360 поиск) как альтернативный источник того же семейства,
/// если разметка Sogou изменится сильнее, чем позволяют резервные селекторы. Этот вариант
/// резервными селекторами. Дополнительные поисковые движки здесь не реализуются, чтобы сохранить область проекта ограниченной.
/// </summary>
public static class SogouSearch
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

        var url = $"https://www.sogou.com/web?query={Uri.EscapeDataString(query)}";

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?>
            {
                ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.7",
                ["Referer"] = "https://www.sogou.com/"
            },
            cancellationToken);

        if (LooksLikeChallenge(page))
        {
            Console.Error.WriteLine("[WebSearch] Sogou: challenge/blocked page detected.");
            return result;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(page);

        // Классическая структура результатов Sogou использует <div class="vrwrap"> или <div class="rb">
        // per результат, с заголовок ссылка в h3 > один. Layered резервные варианты для изменение.
        var nodes = doc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' vrwrap ')]" +
            "|//div[contains(concat(' ', normalize-space(@class), ' '), ' rb ')]" +
            "|//div[contains(@class,'results')]/div[contains(@class,'vr')]" +
            "|//div[.//h3//a[@href]]");

        if (nodes == null || nodes.Count == 0)
        {
            Console.Error.WriteLine("[WebSearch] Sogou: no result nodes matched.");
            return result;
        }

        foreach (var node in nodes)
        {
            if (result.Count >= top)
                break;

            var linkNode =
                node.SelectSingleNode(".//h3//a[@href]") ??
                node.SelectSingleNode(".//a[contains(@class,'title')]") ??
                node.SelectSingleNode(".//a[@href]");

            if (linkNode == null)
                continue;

            var href = WebUtility.HtmlDecode(linkNode.GetAttributeValue("href", string.Empty)).Trim();

            if (href.StartsWith("//"))
                href = "https:" + href;

            // Sogou обычно передаёт ссылки результатов через перенаправление /ссылка?url=....
            if (href.Contains("sogou.com/link?", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(href, @"[?&]url=([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    href = Uri.UnescapeDataString(match.Groups[1].Value);
            }

            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
                continue;

            var titleNode =
                node.SelectSingleNode(".//h3") ??
                linkNode;

            var title = CleanNodeText(titleNode);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var snippetNode =
                node.SelectSingleNode(".//div[contains(@class,'str-info') or contains(@class,'space-txt') or contains(@class,'fz-mid')]") ??
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

        Console.Error.WriteLine($"[WebSearch] Sogou parser matched {result.Count} result(s).");
        return result;
    }

    private static bool LooksLikeChallenge(string html)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(html ?? string.Empty), @"\s+", " ").ToLowerInvariant();
        return text.Contains("验证码") ||          // "verification code" (captcha)
               text.Contains("captcha") ||
               text.Contains("网络异常") ||         // "network anomaly" (Sogou's bot-block page)
               text.Contains("访问异常") ||         // "access anomaly"
               text.Contains("access denied");
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
