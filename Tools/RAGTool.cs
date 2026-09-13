using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Services;

public sealed class RAGTool(RagIndexService rag)
{
    [McpServerTool(Name = "rag_search")]
    [Description("Performs semantic search over local PDF, DOCX, XLSX, PPTX and text/code files.")]
    public async Task<string> RagSearch(
        string path,
        string query,
        int limit = 3,
        double threshold = 0.2,
        CancellationToken cancellationToken = default)
    {
        var results = await rag.SearchAsync(path, query, limit, threshold, cancellationToken);
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }
}
