using System.Net;
using System.Net.Http.Headers;

/// <summary>
/// Предоставляет общие вспомогательные методы HTTP GET и form-urlencoded POST для провайдеров веб-поиска.
/// </summary>
public static class WebPageLoader
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        // Brotli-декомпрессия обязательна: некоторые поисковые системы сжимают ответ через
        // Brotli независимо от заголовка Accept-Encoding. Без AutomaticDecompression с флагом
        // Brotli такой ответ читается как необработанные сжатые байты вместо текста,
        // HtmlAgilityPack не находит в нём ожидаемых узлов, и соответствующий движок молча
        // возвращает ноль результатов. Baidu это не затрагивает, так как он запрашивается
        // как необработанный JSON API по отдельному пути.
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
    /// Отправляет JSON POST-запрос — в отличие от <see cref="Post"/>, который использует
    /// form-urlencoded кодирование — и возвращает необработанное тело ответа.
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

                // Content-Type должен находиться в request.Content.Headers, а не в
                // request.Headers — если попытаться задать его через TryAddWithoutValidation
                // на request.Headers ниже, заголовок будет молча отброшен HttpClient.
                // Здесь он уже выставлен корректно через StringContent выше, поэтому
                // Content-Type из переданных headers просто пропускается.
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
    /// Читает тело ответа независимо от статус-кода. Поисковые системы часто отвечают на похожие
    /// на запросы бота обращения кодами 202/403/429 и страницей CAPTCHA или проверки в теле; выброс
    /// исключения для статусов, отличных от 2xx (как это делает EnsureSuccessStatusCode), отбросил бы
    /// страницу раньше, чем вызывающий код сможет её проверить для диагностики, и все такие случаи
    /// выглядели бы как одна обобщённая сетевая ошибка.
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
