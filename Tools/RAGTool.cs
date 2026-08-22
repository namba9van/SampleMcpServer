#pragma warning disable KMEXP00

using ModelContextProtocol.Server;
using Services;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
/// <summary>
/// Provides an MCP tool for searching the persistent local RAG index.
/// </summary>
public partial class RAGTool
{
    /// <summary>
    /// Ensures the local RAG index is current and searches it using vector similarity.
    /// </summary>
    /// <param name="path">A file or directory containing documents to index.</param>
    /// <param name="query">The natural-language search query.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="threshold">The minimum similarity score a result must reach.</param>
    /// <param name="ragIndexService">The application-managed persistent RAG index service.</param>
    /// <returns>A JSON array containing matching file names, scores, and content, or an error result.</returns>
    [McpServerTool]
    [Description("Searches the persistent local RAG index and returns the most relevant document chunks.")]

    public async Task<string> RagSearch(
        [Description("A file or directory to include in the RAG index.")]
        string path,

        [Description("The natural-language query to search for.")]
        string query,

        [Description("Maximum number of results to return.")]
        int limit = 3,

        [Description("Minimum similarity score required for a result.")]
        double threshold = 0.2,

        RagIndexService ragIndexService = null!
    )
    {
        var results =
            new List<RagResult>();

        try
        {
            Console.Error.WriteLine(
                "=== RAG SEARCH START ===");

            Console.Error.WriteLine(
                $"RAG path: {path}");

            Console.Error.WriteLine(
                $"RAG query: {query}");

            await ragIndexService
                .EnsureIndexUpToDateAsync(
                    path);

            var searchResults =
                await ragIndexService
                    .SearchAsync(
                        query,
                        limit,
                        threshold);

            Console.Error.WriteLine(
                $"Search results count: " +
                $"{searchResults.Count}");

            foreach (var item in searchResults)
            {
                results.Add(
                    new RagResult
                    {
                        FileName =
                            item.Record.FileName ??
                            string.Empty,

                        Score =
                            item.Score,

                        Content =
                            item.Record.Content ??
                            string.Empty
                    });
            }

            Console.Error.WriteLine(
                "=== RAG SEARCH END ===");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"RAG ERROR: {ex}");

            results.Add(
                new RagResult
                {
                    Content =
                        $"Error performing RAG search: " +
                        $"{ex.Message}"
                });
        }

        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true,

                Encoder =
                    JavaScriptEncoder.Create(
                        System.Text.Unicode
                            .UnicodeRanges.All)
            };

        return JsonSerializer.Serialize(
            results,
            options);
    }
    /// <summary>
    /// Represents one result returned by the RAG search tool.
    /// </summary>
    /// <summary>
    /// Represents one result returned by the RAG search tool.
    /// </summary>
    public class RagResult
    {
        /// <summary>
        /// Gets or sets the matched document content.
        /// </summary>
        public string Content { get; set; } =
            string.Empty;

        /// <summary>
        /// Gets or sets the source file name.
        /// </summary>
        public string FileName { get; set; } =
            string.Empty;

        /// <summary>
        /// Gets or sets the similarity score returned by the vector search.
        /// </summary>
        public double? Score { get; set; }
    }
}
