using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using static WebPageLoader;
/// <summary>
/// Provides an MCP tool for searching configured web search engines.
/// </summary>
internal class InternetSearchTools
{
    /// <summary>
    /// Executes the query against the search engines listed in <c>WEB_SEARCH_ENGINES</c>.
    /// </summary>
    /// <param name="query">The web search query.</param>
    /// <returns>A JSON array containing the aggregated search results.</returns>
    [McpServerTool]
    [Description("Searches the configured web search engines and returns aggregated results.")]

    public async Task<string> WebSearch(
        [Description("The web search query.")] string query)
    {
        var searchEngines = Environment.GetEnvironmentVariable("WEB_SEARCH_ENGINES");
        var FirecrawApiKey = Environment.GetEnvironmentVariable("WEB_SEARCH_FirecrawApiKey");
        var duckduckgoRegion = Environment.GetEnvironmentVariable("WEB_SEARCH_duckduckgoRegion");

        var result = new List<SearchResultItem>();
        try
        {
            string[]? engines = searchEngines?.Split(",");

            foreach (string? engine in engines)
            {
                if (engine.ToLower().Contains("duckduckgo")) result.AddRange(await DuckDuckGoSearch.LoadAsync(query, duckduckgoRegion));
                if (engine.ToLower().Contains("firecraw")) result.AddRange(await FirecrawlSearch.LoadAsync(query, FirecrawApiKey));
                if (engine.ToLower().Contains("baidu")) result.AddRange(await BaiduSearch.LoadAsync(query, top:5));
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Create(new TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All)) };
        return System.Text.Json.JsonSerializer.Serialize(result, options);
    }
}
