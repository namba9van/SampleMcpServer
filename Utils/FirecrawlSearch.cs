using Firecrawl;
using static WebPageLoader;
/// <summary>
/// Provides access to Firecrawl search-and-scrape results.
/// </summary>
public static class FirecrawlSearch
{
    /// <summary>
    /// Executes a Firecrawl search and maps the returned entries to common search-result items.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="apiKey">The Firecrawl API key.</param>
    /// <returns>The parsed search results.</returns>
    public static async Task<List<SearchResultItem>> LoadAsync(string query, string apiKey)
    {
        var result = new List<SearchResultItem>();

        var client = new FirecrawlApp(apiKey);

        var searchResults = await client.Search.SearchAndScrapeAsync(query);

        foreach (var data in searchResults.Data)
        {
            result.Add(new SearchResultItem { Title = data.Title, Link = data.Url, Content = data.Description });
        }

        return result;
    }
}
