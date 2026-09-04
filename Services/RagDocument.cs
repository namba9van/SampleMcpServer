using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;
using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// Представляет один фрагмент RAG-документа вместе с его эмбеддингом.
/// </summary>
public sealed class RagDocument
{
    /// <summary>
    /// Получает или задаёт уникальный идентификатор фрагмента документа.
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
    /// Получает или задаёт вектор эмбеддинга, связанный с содержимым.
    /// </summary>
    [JsonIgnore]
    [VectorStoreVector(768)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
