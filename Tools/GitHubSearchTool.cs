using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

public sealed class GitHubSearchTool
{
    private readonly HttpClient _httpClient;

    public GitHubSearchTool()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SampleMcpServer", "2.0"));
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GUTHUB_TOKEN"); // Backward compatibility with the upstream typo.
        if (!string.IsNullOrWhiteSpace(token))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [McpServerTool(Name = "github_search_repositories")]
    [Description("Searches public or token-accessible GitHub repositories.")]
    public async Task<string> SearchRepositories(string query, string codeLanguage = "", int limit = 3, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        var q = string.IsNullOrWhiteSpace(codeLanguage) ? query : $"{query} language:{codeLanguage}";
        var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(q)}&per_page={limit}&sort=stars&order=desc";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        var rows = doc.RootElement.GetProperty("items").EnumerateArray().Select(item => new
        {
            name = item.GetProperty("full_name").GetString(),
            description = item.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null,
            url = item.GetProperty("html_url").GetString()
        });
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "github_search_code")]
    [Description("Searches GitHub code and returns the matched files with decoded source content. A GitHub token is normally required for code search.")]
    public async Task<string> SearchCode(string query, string repo = "", string codeLanguage = "", int limit = 3, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 10);
        var terms = new List<string> { query };
        if (!string.IsNullOrWhiteSpace(repo)) terms.Add($"repo:{repo}");
        if (!string.IsNullOrWhiteSpace(codeLanguage)) terms.Add($"language:{codeLanguage}");
        var url = $"https://api.github.com/search/code?q={Uri.EscapeDataString(string.Join(' ', terms))}&per_page={limit}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(body);
        var results = new List<object>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var apiUrl = item.GetProperty("url").GetString()!;
            using var contentResponse = await _httpClient.GetAsync(apiUrl, cancellationToken);
            var contentBody = await contentResponse.Content.ReadAsStringAsync(cancellationToken);
            contentResponse.EnsureSuccessStatusCode();
            using var contentDoc = JsonDocument.Parse(contentBody);
            var encoded = contentDoc.RootElement.GetProperty("content").GetString()?.Replace("\n", string.Empty) ?? string.Empty;
            var source = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            results.Add(new
            {
                repository = item.GetProperty("repository").GetProperty("full_name").GetString(),
                file = item.GetProperty("name").GetString(),
                path = item.GetProperty("path").GetString(),
                source
            });
        }
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }
}
