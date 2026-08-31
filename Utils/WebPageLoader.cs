using System.Net;
using System.Net.Http.Headers;

/// <summary>
/// Предоставляет общий HTTP GET и закодированный формой POST вспомогательные методы для веб поиск провайдеры.
/// </summary>
public static class WebPageLoader
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        // Техническое примечание к реализации.
        // Техническое примечание к реализации.
        // читаются как обычный текст, HtmlAgilityPack не находит узлы, и каждый из
        // those движки silently возвращает ноль результаты. Baidu является unaffected
        // потому что он запрашивается как необработанный JSON API по отдельному пути.
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                      DecompressionMethods.Deflate |
                                      DecompressionMethods.Brotli
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/131.0.0.0 Safari/537.36");

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    public static async Task<string> Post(
        string url,
        TimeSpan timeout,
        Dictionary<string, string> postData,
        Dictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(postData)
        };

        if (headers != null)
        {
            foreach (var item in headers)
            {
                if (!string.IsNullOrWhiteSpace(item.Value))
                    request.Headers.TryAddWithoutValidation(item.Key, item.Value);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeoutCts.Token);

        return await ReadBodyAsync(response, url, timeoutCts.Token);
    }

    /// <summary>
    /// Отправляет JSON POST-запрос, в отличие от <see cref="Post"/>, который использует
    /// form-urlencoded) и возвращает необработанный ответ тело.
    /// </summary>
    public static async Task<string> PostJson(
        string url,
        TimeSpan timeout,
        string jsonBody,
        Dictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        };

        if (headers != null)
        {
            foreach (var item in headers)
            {
                if (string.IsNullOrWhiteSpace(item.Value))
                    continue;

                // Content-Type должен находиться в запроса.Content.Headers, а не в запроса.Headers.
                // Техническое примечание к реализации.
                // Техническое примечание к реализации.
                if (string.Equals(item.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                request.Headers.TryAddWithoutValidation(item.Key, item.Value);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeoutCts.Token);

        return await ReadBodyAsync(response, url, timeoutCts.Token);
    }

    public static async Task<string> Get(
        string url,
        TimeSpan timeout,
        Dictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (headers != null)
        {
            foreach (var item in headers)
            {
                if (!string.IsNullOrWhiteSpace(item.Value))
                    request.Headers.TryAddWithoutValidation(item.Key, item.Value);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeoutCts.Token);

        return await ReadBodyAsync(response, url, timeoutCts.Token);
    }

    /// <summary>
    /// Читает ответ тело независимо из статус код. поиск движки обычно отвечают
    /// похожие на запросы бота запросы с кодами 202/403/429 и страницей CAPTCHA или проверки в теле; выбрасывание
    /// для статусов, отличных от 2xx, как это делает EnsureSuccessStatusCode, отбрасывает страницу до того, как вызывающий код сможет
    /// проверить it для диагностика, и makes every block look like один generic сетевая ошибка.
    /// </summary>
    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"[WebSearch] Non-success status {(int)response.StatusCode} " +
                $"{response.StatusCode} from {url} (body length {body.Length}).");
        }

        return body;
    }

    /// <summary>
    /// Представляет один нормализованный результат веб-поиска.
    /// </summary>
    public class SearchResultItem
    {
        public string? Title { get; set; }
        public string? Link { get; set; }
        public string? Content { get; set; }
        public string? Engine { get; set; }
    }
}
