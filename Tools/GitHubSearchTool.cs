using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
/// <summary>
/// Provides MCP tools for searching GitHub repositories and source code.
/// </summary>
public class GitHubSearchTool
{
    private readonly HttpClient httpClient;
    /// <summary>
    /// Initializes the GitHub HTTP client and optionally configures authentication
    /// from the <c>GUTHUB_TOKEN</c> environment variable.
    /// </summary>
    public GitHubSearchTool()
    {
        var githubToken = Environment.GetEnvironmentVariable("GUTHUB_TOKEN");

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyGitHubSearchApp", "1.0"));
        if (!string.IsNullOrEmpty(githubToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", githubToken);
        }
    }
    /// <summary>
    /// Searches GitHub repositories and returns the most-starred matches.
    /// </summary>
    /// <param name="query">The GitHub repository search query.</param>
    /// <param name="codeLanguage">An optional GitHub language qualifier.</param>
    /// <param name="limit">The maximum number of repositories to return.</param>
    /// <returns>A formatted text representation of matching repositories.</returns>
    [McpServerTool]
    [Description("Searches GitHub repositories and returns the top matches sorted by stars.")]

    public async Task<string> SearchRepositories(
        [Description("The repository search query.")] string query,
        [Description("Optional programming language filter.")] string codeLanguage = "",
        [Description("Maximum number of results to return.")] int limit = 3)
    {
        try
        {

            var queryString = Uri.EscapeDataString(query);
            if (!string.IsNullOrEmpty(codeLanguage)) queryString = queryString + "+" + Uri.EscapeDataString($"language:{codeLanguage}");

            var url = $"https://api.github.com/search/repositories?q={queryString}&per_page={limit}&sort=stars&order=desc";

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonString);

            var results = new List<string>();
            foreach (var item in json["items"])
            {
                var repoName = item["full_name"]?.ToString() ?? "";
                var repoDescription = item["description"]?.ToString() ?? "";
                var repoUrl = item["html_url"]?.ToString() ?? "";

                results.Add($"Repository: {repoName}\nDescription: {repoDescription}\nURL: {repoUrl}");
            }

            return results.Count > 0
                ? string.Join("\n\n", results)
                : $"No repositories found for '{query}'";
        }
        catch (Exception ex)
        {
            return $"Error searching repositories: {ex.Message}";
        }
    }
    /// <summary>
    /// Searches GitHub code and retrieves the source of matching files.
    /// </summary>
    /// <param name="query">The GitHub code search query.</param>
    /// <param name="repo">An optional repository qualifier.</param>
    /// <param name="codeLanguage">An optional language qualifier.</param>
    /// <param name="limit">The maximum number of files to return.</param>
    /// <returns>A formatted text representation containing repository names, file names, and source code.</returns>
    [McpServerTool]
    [Description("Searches GitHub source code and returns the matching files with their contents.")]

    public async Task<string> SearchCode(
        [Description("The code search query.")] string query,
        [Description("Optional repository qualifier, such as owner/name.")] string repo = "",
        [Description("Optional programming language filter.")] string codeLanguage = "",
        [Description("Maximum number of results to return.")] int limit = 3
        )
    {
        try
        {
            var queryString = Uri.EscapeDataString(query);
            if (!string.IsNullOrEmpty(repo)) queryString = queryString + "+" + Uri.EscapeDataString($"language:{repo}");
            if (!string.IsNullOrEmpty(codeLanguage)) queryString = queryString + "+" + Uri.EscapeDataString($"language:{codeLanguage}");

            var url = $"https://api.github.com/search/code?q={queryString}&per_page={limit}&sort=stars&order=desc";

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonString);

            var items = new List<CodeSearch>();
            foreach (var item in json["items"])
            {
                var repoName = item["repository"]?["full_name"]?.ToString() ?? "";
                var fileName = item["name"]?.ToString() ?? "";
                var fileUrl = item["url"]?.ToString() ?? "";

                items.Add(new CodeSearch() { RepoName = repoName, FileName = fileName, FileUrl = fileUrl });
            }

            var results = new List<string>();
            foreach (var item in items)
            {
                var res = await httpClient.GetAsync(item.FileUrl);
                res.EnsureSuccessStatusCode();

                var jsonString2 = await res.Content.ReadAsStringAsync();
                var json2 = JObject.Parse(jsonString2);

                var content = json2["content"].ToString();
                byte[] data = Convert.FromBase64String(content);
                string source = Encoding.UTF8.GetString(data);

                results.Add($"Repository: {item.RepoName}\nfileName: {item.FileName}\nSource: {source}");
            }

            return results.Count > 0
                    ? string.Join("\n\n", results)
                    : $"No repositories found for '{query}'";
        }
        catch (Exception ex)
        {
            return $"Error searching repositories: {ex.Message}";
        }
    }
    /// <summary>
    /// Holds the identifiers needed to retrieve a GitHub code-search result.
    /// </summary>
    private sealed class CodeSearch
    {
        /// <summary>
        /// Gets or sets the full repository name.
        /// </summary>
        public string RepoName { get; set; }

        /// <summary>
        /// Gets or sets the matched file name.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the GitHub API URL used to retrieve the file contents.
        /// </summary>
        public string FileUrl { get; set; }

    }
}
