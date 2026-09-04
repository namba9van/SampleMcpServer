using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
/// <summary>
/// Предоставляет MCP инструменты для поиска репозиториев и исходного кода GitHub.
/// </summary>
public class GitHubSearchTool
{
    private readonly HttpClient httpClient;

    /// <summary>
    /// Настраивает HTTP-клиент для GitHub API, добавляя авторизацию по токену из
    /// переменной окружения GITHUB_TOKEN, если он задан.
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
    /// Выполняет поиск репозиториев GitHub и возвращает наиболее подходящие совпадения.
    /// </summary>
    /// <param name="query">Поисковый запрос для репозиториев GitHub.</param>
    /// <param name="codeLanguage">Необязательный квалификатор языка GitHub.</param>
    /// <param name="limit">Максимальное количество репозиториев для возврата.</param>
    /// <returns>Отформатированное текстовое представление подходящих репозиториев.</returns>
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
                return $"Ошибка GitHub API ({(int)response.StatusCode}): {json["message"]?.ToString() ?? jsonString}";

            if (json["items"] is not JArray itemsArray)
                return $"Ошибка GitHub API: {json["message"]?.ToString() ?? jsonString}";

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
                : $"По запросу '{query}' репозитории не найдены.";
        }
        catch (Exception ex)
        {
            return $"Ошибка поиска репозиториев: {ex.Message}";
        }
    }

    /// <summary>
    /// Выполняет поиск исходного кода GitHub и получает содержимое подходящих файлов.
    /// </summary>
    /// <param name="query">Поисковый запрос для исходного кода GitHub.</param>
    /// <param name="repo">Необязательный квалификатор репозитория.</param>
    /// <param name="codeLanguage">Необязательный квалификатор языка.</param>
    /// <param name="limit">Максимальное количество файлов для возврата.</param>
    /// <returns>Отформатированное текстовое представление, содержащее имена репозиториев, имена файлов и исходный код.</returns>
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
                return $"Ошибка GitHub API ({(int)response.StatusCode}): {json["message"]?.ToString() ?? jsonString}";

            if (json["items"] is not JArray itemsArray)
                return $"Ошибка GitHub API: {json["message"]?.ToString() ?? jsonString}";

            var items = new List<CodeSearch>();
            foreach (var item in itemsArray)
            {
                var repoName = item["repository"]?["full_name"]?.ToString() ?? "";
                var fileName = item["name"]?.ToString() ?? "";
                var fileUrl = item["url"]?.ToString() ?? "";

                items.Add(new CodeSearch() { RepoName = repoName, FileName = fileName, FileUrl = fileUrl });
            }

            // Каждый файл запрашивается отдельным HTTP-вызовом. Ошибка по ОДНОМУ файлу
            // (сетевой сбой, лимит скорости, файл удалён с момента поиска) не должна ронять
            // весь результат — иначе уже полученные данные по остальным файлам терялись бы
            // из-за единственного неудачного запроса.
            var results = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    var res = await httpClient.GetAsync(item.FileUrl);
                    res.EnsureSuccessStatusCode();

                    var jsonString2 = await res.Content.ReadAsStringAsync();
                    var json2 = JObject.Parse(jsonString2);

                    if (json2["content"] is not JValue { Type: Newtonsoft.Json.Linq.JTokenType.String } contentToken)
                    {
                        results.Add($"Repository: {item.RepoName}\nfileName: {item.FileName}\nSource: (недоступно — файл, скорее всего, превышает лимит GitHub Content API в 1 МБ)");
                        continue;
                    }

                    byte[] data = Convert.FromBase64String(contentToken.ToString());
                    string source = Encoding.UTF8.GetString(data);

                    results.Add($"Repository: {item.RepoName}\nfileName: {item.FileName}\nSource: {source}");
                }
                catch (Exception ex)
                {
                    results.Add($"Repository: {item.RepoName}\nfileName: {item.FileName}\nSource: (ошибка получения содержимого: {ex.Message})");
                }
            }

            return results.Count > 0
                    ? string.Join("\n\n", results)
                    : $"По запросу '{query}' файлы не найдены.";
        }
        catch (Exception ex)
        {
            return $"Ошибка поиска кода: {ex.Message}";
        }
    }

    /// <summary>
    /// Хранит идентификаторы, необходимые для получения одного результата поиска кода GitHub.
    /// </summary>
    private sealed class CodeSearch
    {
        /// <summary>
        /// Получает или задаёт полное имя репозитория.
        /// </summary>
        public string RepoName { get; set; } = string.Empty;

        /// <summary>
        /// Получает или задаёт имя найденного файла.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Получает или задаёт URL GitHub API, используемый для получения содержимого файла.
        /// </summary>
        public string FileUrl { get; set; } = string.Empty;

    }
}
