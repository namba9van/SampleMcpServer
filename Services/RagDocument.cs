using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;
using System.Text.Json.Serialization;

namespace Services;

public sealed class RagDocument
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [VectorStoreKey]
    [TextSearchResultName]
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    [VectorStoreData]
    [TextSearchResultValue]
    public string? Content { get; init; }

    public string? FileName { get; init; }

    [JsonIgnore]
    [VectorStoreVector(768)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}