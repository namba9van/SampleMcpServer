using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

public sealed partial class InternetSearchTools
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    [McpServerTool(Name = "internet_search")]
    [Description("Searches the web using the engines configured in WEB_SEARCH_ENGINES. Supported engines: DuckDuckGo and Firecrawl.")]
    public async Task<string> WebSearch(string query, int limit = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query is required", nameof(query));

        limit = Math.Clamp(limit, 1, 20);
        var engines = (Environment.GetEnvironmentVariable("WEB_SEARCH_ENGINES") ?? "DuckDuckGo")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<WebSearchResult>();

        foreach (var engine in engines)
        {
            if (engine.Equals("DuckDuckGo", StringComparison.OrdinalIgnoreCase))
                results.AddRange(await SearchDuckDuckGoAsync(query, limit, cancellationToken));
            else if (engine.Equals("Firecrawl", StringComparison.OrdinalIgnoreCase))
                results.AddRange(await SearchFirecrawlAsync(query, limit, cancellationToken));
        }

        var unique = results
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .GroupBy(r => r.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(limit)
            .ToArray();

        return JsonSerializer.Serialize(unique, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<IEnumerable<WebSearchResult>> SearchDuckDuckGoAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var region = Environment.GetEnvironmentVariable("WEB_SEARCH_DUCKDUCKGO_REGION")
            ?? Environment.GetEnvironmentVariable("WEB_SEARCH_duckduckgoRegion")
            ?? "wt-wt";
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}&kl={Uri.EscapeDataString(region)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 SampleMcpServer/2.0");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var results = new List<WebSearchResult>();
        foreach (Match match in DuckResultRegex().Matches(html))
        {
            var rawUrl = WebUtility.HtmlDecode(match.Groups[1].Value);
            var title = StripHtml(WebUtility.HtmlDecode(match.Groups[2].Value));
            var decodedUrl = DecodeDuckDuckGoRedirect(rawUrl);
            if (!string.IsNullOrWhiteSpace(title) && Uri.TryCreate(decodedUrl, UriKind.Absolute, out _))
                results.Add(new WebSearchResult(title, decodedUrl, string.Empty, "DuckDuckGo"));
            if (results.Count >= limit) break;
        }
        return results;
    }

    private async Task<IEnumerable<WebSearchResult>> SearchFirecrawlAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_FIRECRAWL_API_KEY")
            ?? Environment.GetEnvironmentVariable("WEB_SEARCH_FirecrawApiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
            return Array.Empty<WebSearchResult>();

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.firecrawl.dev/v1/search");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { query, limit });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        var results = new List<WebSearchResult>();
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return results;
        foreach (var item in data.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            results.Add(new WebSearchResult(title, url, description, "Firecrawl"));
        }
        return results;
    }

    private static string DecodeDuckDuckGoRedirect(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;
        if (!uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
            return url;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "uddg")
                return Uri.UnescapeDataString(parts[1]);
        }
        return url;
    }

    private static string StripHtml(string value) => Regex.Replace(value, "<[^>]+>", string.Empty).Trim();

    [GeneratedRegex(@"class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DuckResultRegex();
}

public sealed record WebSearchResult(string Title, string Url, string Description, string Engine);
