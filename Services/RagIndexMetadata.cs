using System.Text.Json.Serialization;

namespace Services;

public sealed class RagIndexMetadata
{
    public int Version { get; set; } = 2;

    public string EmbeddingModel { get; set; } =
        string.Empty;

    public string EmbeddingEndpoint { get; set; } =
        string.Empty;

    public int EmbeddingDimension { get; set; }

    public Dictionary<string, RagFileMetadata> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public int DocumentCount =>
        Files.Values.Sum(x => x.Sections.Count);
}

public sealed class RagFileMetadata
{
    public string FilePath { get; set; } =
        string.Empty;

    public string Fingerprint { get; set; } =
        string.Empty;

    public List<RagSectionMetadata> Sections { get; set; } =
        new();
}

public sealed class RagSectionMetadata
{
    public string Id { get; set; } =
        string.Empty;

    public string Content { get; set; } =
        string.Empty;

    public float[] Embedding { get; set; } =
        Array.Empty<float>();
}