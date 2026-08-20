// https://github.com/virex-84

#pragma warning disable KMEXP00

using ModelContextProtocol.Server;
using Services;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;

public partial class RAGTool
{
    [McpServerTool]
    [Description("Performs a RAG search using local documents.")]
    public async Task<string> RagSearch(
        [Description("The path of the files for search")]
        string path,

        [Description("The search query")]
        string query,

        [Description("The retrieval limit")]
        int limit = 3,

        [Description("The retrieval affinity threshold")]
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

    public class RagResult
    {
        public string Content { get; set; } =
            string.Empty;

        public string FileName { get; set; } =
            string.Empty;

        public double? Score { get; set; }
    }
}