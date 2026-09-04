using Newtonsoft.Json;
using static WebPageLoader;
/// <summary>
/// Предоставляет доступ к результатам веб-поиска Baidu.
/// </summary>
public class BaiduSearch
{
    /// <summary>
    /// Выполняет запрос к Baidu и преобразует непустые сводки в общий формат результатов поиска.
    /// </summary>
    /// <param name="query">Поисковый запрос.</param>
    /// <param name="top">Максимальное число результатов, запрашиваемых у Baidu.</param>
    /// <returns>Соответствующие результаты поиска.</returns>
    public static async Task<IEnumerable<WebPageLoader.SearchResultItem>> LoadAsync(string query, int top)
    {
        var result = new List<SearchResultItem>();

        query = Uri.EscapeDataString(query);

        var cr = "ru";

        var headers = new Dictionary<string, string?>() {{ "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" }};
        var page = await WebPageLoader.Get($"https://www.baidu.com/s?wd={query}&tn=json&rn={top}&cr={cr}&ie=utf-8&pn=0", TimeSpan.FromSeconds(30), headers);

        var options = new JsonSerializerSettings { Formatting = Formatting.Indented, StringEscapeHandling=StringEscapeHandling.EscapeHtml };
        var data = JsonConvert.DeserializeObject<Root>(page, options);

        if (data?.feed?.entry == null)
            return result;

        foreach (var resultNode in data.feed.entry)
        {
            if (!string.IsNullOrWhiteSpace(resultNode.abs) &&
                !string.IsNullOrWhiteSpace(resultNode.url))
            {
                result.Add(new SearchResultItem
                {
                    Title = resultNode.title,
                    Link = resultNode.url,
                    Content = resultNode.abs
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Представляет одну запись результата поиска Baidu.
    /// </summary>
    class Entry
    {
        /// <summary>
        /// Получает или задаёт заголовок результата, возвращённый Baidu.
        /// </summary>
        public string? title { get; set; }

        /// <summary>
        /// Получает или задаёт текст аннотации результата.
        /// </summary>
        public string? abs { get; set; }

        /// <summary>
        /// Получает или задаёт URL результата.
        /// </summary>
        public string? url { get; set; }

        /// <summary>
        /// Получает или задаёт закодированный URL результата.
        /// </summary>
        public string? urlEnc { get; set; }

        /// <summary>
        /// Получает или задаёт временную метку результата.
        /// </summary>
        public string? time { get; set; }
    }

    /// <summary>
    /// Представляет ленту результатов, возвращённую Baidu.
    /// </summary>
    class Feed
    {
        /// <summary>
        /// Получает или задаёт записи результатов.
        /// </summary>
        public List<Entry>? entry { get; set; }
    }

    /// <summary>
    /// Представляет корневой объект JSON-ответа Baidu.
    /// </summary>
    class Root
    {
        /// <summary>
        /// Получает или задаёт ленту результатов Baidu.
        /// </summary>
        public Feed? feed { get; set; }
    }
}
