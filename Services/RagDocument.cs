using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;
using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// Represents one RAG document chunk together with its embedding.
/// </summary>
public sealed class RagDocument
{
    /// <summary>
    /// Gets or sets the unique identifier of the document chunk.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [VectorStoreKey]
    [TextSearchResultName]
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or initializes the text content of the document chunk.
    /// </summary>
    [VectorStoreData]
    [TextSearchResultValue]
    public string? Content { get; init; }

    /// <summary>
    /// Gets or initializes the source file path.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Gets or sets the embedding vector associated with the content.
    /// </summary>
    [JsonIgnore]
    [VectorStoreVector(768)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
