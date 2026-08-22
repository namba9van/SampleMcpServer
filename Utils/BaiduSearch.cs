using Newtonsoft.Json;
using static WebPageLoader;
/// <summary>
/// Provides access to Baidu web search results.
/// </summary>
public class BaiduSearch
{
    /// <summary>
    /// Queries Baidu and maps non-empty result summaries to common search-result items.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="top">The maximum number of results requested from Baidu.</param>
    /// <returns>The matching search-result items.</returns>
    public static async Task<IEnumerable<WebPageLoader.SearchResultItem>> LoadAsync(string query, int top)
    {
        var result = new List<SearchResultItem>();

        query = Uri.EscapeDataString(query);

        var cr = "ru";

        var headers = new Dictionary<string, string?>() {{ "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" }};
        var page = await WebPageLoader.Get($"https://www.baidu.com/s?wd={query}&tn=json&rn={top}&cr={cr}&ie=utf-8&pn=0", TimeSpan.FromSeconds(30), headers);

        var options = new JsonSerializerSettings { Formatting = Formatting.Indented, StringEscapeHandling=StringEscapeHandling.EscapeHtml };
        var data = JsonConvert.DeserializeObject<Root>(page, options);
        foreach (var resultNode in data.feed.entry)
        {
            if (!string.IsNullOrEmpty(resultNode.abs))
                result.Add(new SearchResultItem { Title = resultNode.title, Link = resultNode.url, Content = resultNode.abs });
        }

        return result;
    }

    /// <summary>
    /// Represents a Baidu search result entry.
    /// </summary>
    class Entry
    {
        /// <summary>
        /// Gets or sets the result title returned by Baidu.
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// Gets or sets the result abstract text.
        /// </summary>
        public string abs { get; set; }

        /// <summary>
        /// Gets or sets the result URL.
        /// </summary>
        public string url { get; set; }

        /// <summary>
        /// Gets or sets the encoded result URL.
        /// </summary>
        public string urlEnc { get; set; }

        /// <summary>
        /// Gets or sets the result timestamp.
        /// </summary>
        public string time { get; set; }
    }

    /// <summary>
    /// Represents the result feed returned by Baidu.
    /// </summary>
    class Feed
    {
        /// <summary>
        /// Gets or sets the result entries.
        /// </summary>
        public List<Entry> entry { get; set; }
    }

    /// <summary>
    /// Represents the root of the Baidu JSON response.
    /// </summary>
    class Root
    {
        /// <summary>
        /// Gets or sets the Baidu result feed.
        /// </summary>
        public Feed feed { get; set; }
    }
}
