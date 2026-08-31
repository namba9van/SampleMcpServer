using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
/// <summary>
/// Предоставляет MCP инструменты для поиска GitHub репозитории и исходный код.
/// </summary>
public class GitHubSearchTool
{
    private readonly HttpClient httpClient;
    /// <summary>
    /// Описывает назначение элемента.
    /// Описывает назначение элемента.
    /// </summary>
    public GitHubSearchTool()
    {
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyGitHubSearchApp", "1.0"));
        if (!string.IsNullOrEmpty(githubToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", githubToken);
        }
    }
    /// <summary>
    /// Выполняет поиск GitHub репозитории и возвращает most-starred matches.
    /// </summary>
    /// <param name="query">Поисковый запрос для репозиториев GitHub.</param>
    /// <param name="codeLanguage">необязательный GitHub language qualifier.</param>
    /// <param name="limit">Максимальное количество репозиториев для возврата.</param>
    /// <returns>formatted текст representation из соответствующий репозитории.</returns>
    [McpServerTool]
    [Description("Выполняет поиск репозиториев GitHub и возвращает наиболее подходящие результаты, отсортированные по числу звёзд.")]

    public async Task<string> SearchRepositories(
        [Description("Поисковый запрос для репозиториев.")] string query,
        [Description("Необязательный фильтр по языку программирования.")] string codeLanguage = "",
        [Description("Максимальное количество результатов для возврата.")] int limit = 3)
    {
        try
        {

            var queryString = Uri.EscapeDataString(query);
            if (!string.IsNullOrEmpty(codeLanguage)) queryString = queryString + "+" + Uri.EscapeDataString($"language:{codeLanguage}");

            var url = $"https://api.github.com/search/repositories?q={queryString}&per_page={limit}&sort=stars&order=desc";

            var response = await httpClient.GetAsync(url);
            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonString);

            if (!response.IsSuccessStatusCode)
                return $"GitHub API error ({(int)response.StatusCode}): {json["message"]?.ToString() ?? jsonString}";

            if (json["items"] is not JArray itemsArray)
                return $"GitHub API error: {json["message"]?.ToString() ?? jsonString}";

            var results = new List<string>();
            foreach (var item in itemsArray)
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
    /// Выполняет поиск GitHub код и retrieves исходный из соответствующий файлы.
    /// </summary>
    /// <param name="query">Поисковый запрос для исходного кода GitHub.</param>
    /// <param name="repo">необязательный репозиторий qualifier.</param>
    /// <param name="codeLanguage">необязательный language qualifier.</param>
    /// <param name="limit">Максимальное количество файлов для возврата.</param>
    /// <returns>formatted текст representation containing репозиторий names, файл names, и исходный код.</returns>
    [McpServerTool]
    [Description("Выполняет поиск исходного кода GitHub и возвращает подходящие файлы вместе с их содержимым.")]

    public async Task<string> SearchCode(
        [Description("Поисковый запрос для исходного кода.")] string query,
        [Description("Необязательный фильтр репозитория, например owner/name.")] string repo = "",
        [Description("Необязательный фильтр по языку программирования.")] string codeLanguage = "",
        [Description("Максимальное количество результатов для возврата.")] int limit = 3
        )
    {
        try
        {
            var queryString = Uri.EscapeDataString(query);
            if (!string.IsNullOrEmpty(repo)) queryString = queryString + "+" + Uri.EscapeDataString($"repo:{repo}");
            if (!string.IsNullOrEmpty(codeLanguage)) queryString = queryString + "+" + Uri.EscapeDataString($"language:{codeLanguage}");

            var url = $"https://api.github.com/search/code?q={queryString}&per_page={limit}&sort=stars&order=desc";

            var response = await httpClient.GetAsync(url);
            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonString);

            if (!response.IsSuccessStatusCode)
                return $"GitHub API error ({(int)response.StatusCode}): {json["message"]?.ToString() ?? jsonString}";

            if (json["items"] is not JArray itemsArray)
                return $"GitHub API error: {json["message"]?.ToString() ?? jsonString}";

            var items = new List<CodeSearch>();
            foreach (var item in itemsArray)
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

                if (json2["content"] is not JValue { Type: Newtonsoft.Json.Linq.JTokenType.String } contentToken)
                {
                    results.Add($"Repository: {item.RepoName}\nfileName: {item.FileName}\nSource: (unavailable — file likely exceeds GitHub's 1MB content-API limit)");
                    continue;
                }

                byte[] data = Convert.FromBase64String(contentToken.ToString());
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
    /// Хранит идентификаторы, необходимые для получения одного результата поиска кода GitHub.
    /// </summary>
    private sealed class CodeSearch
    {
        /// <summary>
        /// Получает или задаёт full репозиторий имя.
        /// </summary>
        public string RepoName { get; set; } = string.Empty;

        /// <summary>
        /// Получает или задаёт matched файл имя.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Получает или задаёт URL GitHub API, используемый для получения содержимого файла.
        /// </summary>
        public string FileUrl { get; set; } = string.Empty;

    }
}
