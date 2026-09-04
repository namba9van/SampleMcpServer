using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;
using static WebPageLoader;

/// <summary>
/// Предоставляет доступ к HTML-поиску DuckDuckGo и его endpoint для быстрых ответов.
/// </summary>
public static class DuckDuckGoSearch
{
    public static async Task<List<SearchResultItem>> LoadAsync(
        string query,
        string? region = null,
        string? time = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SearchResultItem>();

        var postData = new Dictionary<string, string> { ["q"] = query };
        if (!string.IsNullOrEmpty(region)) postData["kl"] = region;
        if (!string.IsNullOrEmpty(time)) postData["df"] = time;

        var page = await WebPageLoader.Post(
            "https://html.duckduckgo.com/html/",
            TimeSpan.FromSeconds(20),
            postData,
            new Dictionary<string, string?>
            {
                ["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.7",
                ["Referer"] = "https://duckduckgo.com/",
                ["Origin"] = "https://html.duckduckgo.com"
            },
            cancellationToken);

        var doc = new HtmlDocument();
        doc.LoadHtml(page);

        var resultNodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class, 'result') and .//a[contains(@class,'result__a')]]");

        if (resultNodes == null || resultNodes.Count == 0)
        {
            Console.Error.WriteLine("[WebSearch] DuckDuckGo POST returned no result nodes; trying GET fallback.");
            return await LoadGetAsync(query, region, cancellationToken);
        }

        foreach (var resultNode in resultNodes)
        {
            var titleNode = resultNode.SelectSingleNode(".//a[contains(@class,'result__a')]");
            var linkNode = titleNode;
            var snippetNode = resultNode.SelectSingleNode(".//*[contains(@class,'result__snippet')]");

            var title = titleNode == null ? string.Empty : CleanNodeText(titleNode);
            var link = linkNode?.GetAttributeValue("href", string.Empty);
            var content = snippetNode == null ? string.Empty : CleanNodeText(snippetNode);

            if (!TryNormalizeUrl(link, out var normalizedLink) ||
                string.IsNullOrWhiteSpace(title))
                continue;

            if (result.Any(x => string.Equals(
                    x.Link,
                    normalizedLink,
                    StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new SearchResultItem
            {
                Title = title,
                Link = normalizedLink,
                Content = content
            });
        }

        return result;
    }

    private static async Task<List<SearchResultItem>> LoadGetAsync(
        string query,
        string? region,
        CancellationToken cancellationToken)
    {
        var result = new List<SearchResultItem>();
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}" +
                  (string.IsNullOrWhiteSpace(region) ? string.Empty : $"&kl={Uri.EscapeDataString(region)}");

        var page = await WebPageLoader.Get(
            url,
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?>
            {
                ["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.7",
                ["Referer"] = "https://duckduckgo.com/"
            },
            cancellationToken);

        var doc = new HtmlDocument();
        doc.LoadHtml(page);
        var nodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'result') and .//a[contains(@class,'result__a')]]" +
            "|//article[contains(@class,'result')]" );

        if (nodes == null)
        {
            Console.Error.WriteLine("[WebSearch] DuckDuckGo GET fallback returned no result nodes.");
            return result;
        }

        foreach (var node in nodes)
        {
            var titleNode = node.SelectSingleNode(".//a[contains(@class,'result__a')]") ?? node.SelectSingleNode(".//h2//a");
            if (titleNode == null) continue;

            var href = titleNode.GetAttributeValue("href", string.Empty);
            if (!TryNormalizeUrl(href, out var link)) continue;

            var title = CleanNodeText(titleNode);
            if (string.IsNullOrWhiteSpace(title)) continue;

            var snippetNode = node.SelectSingleNode(".//*[contains(@class,'result__snippet')]") ?? node.SelectSingleNode(".//p");
            var content = snippetNode == null ? string.Empty : CleanNodeText(snippetNode);

            if (result.Any(x => string.Equals(x.Link, link, StringComparison.OrdinalIgnoreCase))) continue;

            result.Add(new SearchResultItem { Title = title, Link = link, Content = content });
        }

        Console.Error.WriteLine($"[WebSearch] DuckDuckGo GET fallback parsed {result.Count} result(s).");
        return result;
    }

    public static async Task<List<SearchResultItem>> LoadAsync2(
        string query,
        string? region = null,
        string? time = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SearchResultItem>();

        var encodedQuery = Uri.EscapeDataString(query);

        var page = await WebPageLoader.Get(
            $"https://api.duckduckgo.com/?q={encodedQuery}&format=json&no_redirect=1&no_html=1&skip_disambig=1",
            TimeSpan.FromSeconds(20),
            new Dictionary<string, string?> { ["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.7" },
            cancellationToken);

        var data = JsonConvert.DeserializeObject<Root>(page);

        if (data != null &&
            (!string.IsNullOrWhiteSpace(data.AbstractText) ||
             !string.IsNullOrWhiteSpace(data.Heading)))
        {
            result.Add(new SearchResultItem
            {
                Title = data.Heading,
                Link = data.AbstractURL,
                Content = data.AbstractText
            });
        }

        return result;
    }

    private static bool TryNormalizeUrl(string? href, out string url)
    {
        url = string.Empty;

        if (string.IsNullOrWhiteSpace(href))
            return false;

        href = WebUtility.HtmlDecode(href);

        if (href.StartsWith("//"))
            href = "https:" + href;

        // DuckDuckGo оборачивает внешние ссылки в редирект вида /l/?uddg=<url-encoded-адрес>;
        // извлекаем из него настоящий целевой URL.
        if (href.Contains("duckduckgo.com/l/?", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(
                href,
                @"[?&]uddg=([^&]+)",
                RegexOptions.IgnoreCase);

            if (match.Success)
                href = Uri.UnescapeDataString(match.Groups[1].Value);
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
            return false;

        url = uri.ToString();
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

    private static string CleanText(string? value) =>
        Regex.Replace(WebUtility.HtmlDecode(value ?? string.Empty), @"\s+", " ").Trim();

    public class Root
    {
        public string? AbstractSource { get; set; }
        public string? AbstractText { get; set; }
        public string? AbstractURL { get; set; }
        public string? Heading { get; set; }
    }
}
