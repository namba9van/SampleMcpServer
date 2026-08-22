using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using static WebPageLoader;
/// <summary>
/// Provides access to DuckDuckGo HTML and instant-answer search endpoints.
/// </summary>
public static class DuckDuckGoSearch
{
    /// <summary>
    /// Searches the DuckDuckGo HTML endpoint and extracts result titles, links, and snippets.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="region">An optional DuckDuckGo region code.</param>
    /// <param name="time">An optional time-range filter.</param>
    /// <returns>The parsed search results.</returns>
    public static async Task<List<SearchResultItem>> LoadAsync(string query, string? region = null, string? time = null)
    {
        var result = new List<SearchResultItem>();

        var postData = new Dictionary<string, string>() { { "q", query } };
        if (!string.IsNullOrEmpty(region)) postData.Add("kl", region);
        if (!string.IsNullOrEmpty(time)) postData.Add("df", time);

        var page = await WebPageLoader.Post("https://html.duckduckgo.com/html", TimeSpan.FromSeconds(30), postData);

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(page);

            var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'results_links_deep')]");

            if (resultNodes != null)
            {
                foreach (var resultNode in resultNodes)
                {
                    var title = resultNode.SelectSingleNode(".//a[contains(@class, 'result__a')]")?.InnerText.Trim() ?? string.Empty;
                    var linkNode = resultNode.SelectSingleNode(".//a[contains(@class, 'result__snippet')]");
                    var link = linkNode?.GetAttributeValue("href", string.Empty);
                    var content = linkNode?.InnerText.Trim() ?? string.Empty;

                    if (!string.IsNullOrEmpty(link))
                    {
                        link = link.Replace("//duckduckgo.com/l/?uddg=", "");
                        link = Regex.Replace(link, "&rut=.*", "");
                        link = Uri.UnescapeDataString(link);
                    }

                    result.Add(new SearchResultItem { Title = title, Link = link, Content = content });
                }
            }
        }
        catch (Exception e)
        {
        }

        return result;
    }
    /// <summary>
    /// Queries the DuckDuckGo instant-answer endpoint.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="region">An optional region value retained for API compatibility.</param>
    /// <param name="time">An optional time filter retained for API compatibility.</param>
    /// <returns>A list containing the returned instant-answer summary.</returns>
    public static async Task<List<SearchResultItem>> LoadAsync2(string query, string? region = null, string? time = null)
    {
        var result = new List<SearchResultItem>();

        query = Uri.EscapeDataString(query);

        var headers = new Dictionary<string, string?>() { { "accept-language", "ru" } };
        var page = await WebPageLoader.Get($"https://api.duckduckgo.com/?q={query}&format=json&no_redirect=1&no_html=1&skip_disambig=1", TimeSpan.FromSeconds(30), headers);

        var data = JsonConvert.DeserializeObject<Root>(page);

        result.Add(new SearchResultItem { Title = data.Heading, Link = data.AbstractURL, Content = data.AbstractText });

        return result;
    }
    /// <summary>
    /// Represents the subset of the DuckDuckGo instant-answer response used by the server.
    /// </summary>
    public class Root
    {
        /// <summary>
        /// Gets or sets the source name of the instant answer.
        /// </summary>
        public string AbstractSource { get; set; }

        /// <summary>
        /// Gets or sets the instant-answer summary.
        /// </summary>
        public string AbstractText { get; set; }

        /// <summary>
        /// Gets or sets the canonical result URL.
        /// </summary>
        public string AbstractURL { get; set; }

        /// <summary>
        /// Gets or sets the result heading.
        /// </summary>
        public string Heading { get; set; }
    }

}
