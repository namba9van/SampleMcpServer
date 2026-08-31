using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// Хранит постоянный метаданные требуемый для проверить и перестроить RAG индекс.
/// </summary>
public sealed class RagIndexMetadata
{
    /// <summary>
    /// Получает или задаёт сериализованный метаданные схема версия.
    /// </summary>
    public int Version { get; set; } = 2;

    /// <summary>
    /// Получает или задаёт embedding модель идентификатор используемый для построения индекс.
    /// </summary>
    public string EmbeddingModel { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт endpoint embedding, связанный с индексом.
    /// </summary>
    public string EmbeddingEndpoint { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт ожидаемый embedding вектор размерность.
    /// </summary>
    public int EmbeddingDimension { get; set; }

    /// <summary>
    /// Получает или задаёт метаданные для индексированных файлов, ключом которых является полный путь.
    /// </summary>
    public Dictionary<string, RagFileMetadata> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Получает общее количество число из сохранённый документ разделы.
    /// </summary>
    [JsonIgnore]
    public int DocumentCount =>
        Files.Values.Sum(x => x.Sections.Count);
}

/// <summary>
/// Хранит индексации метаданные для один исходный файл.
/// </summary>
public sealed class RagFileMetadata
{
    /// <summary>
    /// Получает или задаёт индексированный файл путь.
    /// </summary>
    public string FilePath { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт отпечаток, полученный при индексации файла.
    /// </summary>
    public string Fingerprint { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт сохранённый разделы относящиеся для файл.
    /// </summary>
    public List<RagSectionMetadata> Sections { get; set; } =
        new();
}

/// <summary>
/// Хранит сохранённый содержимое и embedding для один RAG раздел.
/// </summary>
public sealed class RagSectionMetadata
{
    /// <summary>
    /// Получает или задаёт сохранённый раздел идентификатор.
    /// </summary>
    public string Id { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт сохранённый раздел содержимое.
    /// </summary>
    public string Content { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт сохранённый embedding-вектор.
    /// </summary>
    public float[] Embedding { get; set; } =
        Array.Empty<float>();
}
