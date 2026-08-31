using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к веб-поиску Yandex без API-ключа.
/// </summary>
public static class YandexSearch
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

        var url = $"https://yandex.ru/search/?text={Uri.EscapeDataString(query)}&lr=213";

        var headers = new Dictionary<string, string?>
        {
            ["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.7"
        };

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            headers,
            cancellationToken);

        var doc = new HtmlDocument();
        doc.LoadHtml(page);

        // Yandex периодически меняет CSS-классы. Используются несколько известных вариантов структуры.
        var nodes = doc.DocumentNode.SelectNodes(
            "//li[contains(@class,'serp-item')]" +
            "|//div[contains(@class,'serp-item')]" +
            "|//div[contains(@class,'organic')]");

        if (nodes == null)
            return result;

        foreach (var node in nodes)
        {
            if (result.Count >= top)
                break;

            var linkNode =
                node.SelectSingleNode(".//a[contains(@class,'organic__url')]") ??
                node.SelectSingleNode(".//a[contains(@class,'Link')]") ??
                node.SelectSingleNode(".//a[@href]");

            if (linkNode == null)
                continue;

            var href = WebUtility.HtmlDecode(
                linkNode.GetAttributeValue("href", string.Empty)).Trim();

            if (!TryGetExternalUrl(href, out var link))
                continue;

            var titleNode =
                node.SelectSingleNode(".//*[contains(@class,'organic__title-wrapper')]") ??
                node.SelectSingleNode(".//*[contains(@class,'organic__title')]") ??
                linkNode;

            var title = CleanNodeText(titleNode);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var contentNode =
                node.SelectSingleNode(".//*[contains(@class,'organic__content-wrapper')]") ??
                node.SelectSingleNode(".//*[contains(@class,'organic__snippet')]");

            var content = CleanNodeText(contentNode ?? node);

            if (content.Length > 700)
                content = content[..700];

            if (result.Any(x => string.Equals(x.Link, link, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new SearchResultItem
            {
                Title = title,
                Link = link,
                Content = content
            });
        }

        return result;
    }

    private static bool TryGetExternalUrl(string href, out string link)
    {
        link = string.Empty;

        if (string.IsNullOrWhiteSpace(href))
            return false;

        if (href.StartsWith("//"))
            href = "https:" + href;

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (uri.Host.EndsWith("yandex.ru", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("ya.ru", StringComparison.OrdinalIgnoreCase))
            return false;

        link = uri.ToString();
        return true;
    }

    private static string CleanNodeText(HtmlNode node)
    {
        var parts = node.DescendantsAndSelf()
            .Where(n => n.NodeType == HtmlNodeType.Text)
            .Select(n => WebUtility.HtmlDecode(n.InnerText).Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return CleanText(string.Join(" ", parts));
    }

    private static string CleanText(string value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
