using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;
using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// Представляет один RAG документ фрагмент together с его embedding.
/// </summary>
public sealed class RagDocument
{
    /// <summary>
    /// Получает или задаёт уникальный идентификатор из документ фрагмент.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [VectorStoreKey]
    [TextSearchResultName]
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    /// <summary>
    /// Получает или инициализирует текстовое содержимое фрагмента документа.
    /// </summary>
    [VectorStoreData]
    [TextSearchResultValue]
    public string? Content { get; init; }

    /// <summary>
    /// Получает или инициализирует путь к исходному файлу.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Получает или задаёт embedding вектор связанный с содержимое.
    /// </summary>
    [JsonIgnore]
    [VectorStoreVector(768)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
