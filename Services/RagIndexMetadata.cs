using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// Stores the persistent metadata required to validate and rebuild the RAG index.
/// </summary>
public sealed class RagIndexMetadata
{
    /// <summary>
    /// Gets or sets the serialized metadata schema version.
    /// </summary>
    public int Version { get; set; } = 2;

    /// <summary>
    /// Gets or sets the embedding model identifier used to build the index.
    /// </summary>
    public string EmbeddingModel { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the embedding endpoint associated with the index.
    /// </summary>
    public string EmbeddingEndpoint { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the expected embedding vector dimension.
    /// </summary>
    public int EmbeddingDimension { get; set; }

    /// <summary>
    /// Gets or sets metadata for indexed files, keyed by full path.
    /// </summary>
    public Dictionary<string, RagFileMetadata> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the total number of persisted document sections.
    /// </summary>
    [JsonIgnore]
    public int DocumentCount =>
        Files.Values.Sum(x => x.Sections.Count);
}

/// <summary>
/// Stores indexing metadata for one source file.
/// </summary>
public sealed class RagFileMetadata
{
    /// <summary>
    /// Gets or sets the indexed file path.
    /// </summary>
    public string FilePath { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the fingerprint captured when the file was indexed.
    /// </summary>
    public string Fingerprint { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the persisted sections belonging to the file.
    /// </summary>
    public List<RagSectionMetadata> Sections { get; set; } =
        new();
}

/// <summary>
/// Stores the persisted content and embedding for one RAG section.
/// </summary>
public sealed class RagSectionMetadata
{
    /// <summary>
    /// Gets or sets the persisted section identifier.
    /// </summary>
    public string Id { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the persisted section content.
    /// </summary>
    public string Content { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the persisted embedding vector.
    /// </summary>
    public float[] Embedding { get; set; } =
        Array.Empty<float>();
}
