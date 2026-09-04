using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// Хранит постоянные метаданные, необходимые для проверки и перестройки индекса RAG.
/// </summary>
public sealed class RagIndexMetadata
{
    /// <summary>
    /// Получает или задаёт версию схемы сериализованных метаданных.
    /// </summary>
    public int Version { get; set; } = 2;

    /// <summary>
    /// Получает или задаёт идентификатор embedding-модели, использованной для построения индекса.
    /// </summary>
    public string EmbeddingModel { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт endpoint эмбеддингов, связанный с индексом.
    /// </summary>
    public string EmbeddingEndpoint { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт ожидаемую размерность вектора эмбеддинга.
    /// </summary>
    public int EmbeddingDimension { get; set; }

    /// <summary>
    /// Получает или задаёт метаданные для индексированных файлов, ключом которых является полный путь.
    /// </summary>
    public Dictionary<string, RagFileMetadata> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Получает общее количество сохранённых разделов документов.
    /// </summary>
    [JsonIgnore]
    public int DocumentCount =>
        Files.Values.Sum(x => x.Sections.Count);
}

/// <summary>
/// Хранит метаданные индексации для одного исходного файла.
/// </summary>
public sealed class RagFileMetadata
{
    /// <summary>
    /// Получает или задаёт путь индексированного файла.
    /// </summary>
    public string FilePath { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт отпечаток, полученный при индексации файла.
    /// </summary>
    public string Fingerprint { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт сохранённые разделы, относящиеся к файлу.
    /// </summary>
    public List<RagSectionMetadata> Sections { get; set; } =
        new();
}

/// <summary>
/// Хранит сохранённое содержимое и эмбеддинг для одного раздела RAG.
/// </summary>
public sealed class RagSectionMetadata
{
    /// <summary>
    /// Получает или задаёт сохранённый идентификатор раздела.
    /// </summary>
    public string Id { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт сохранённое содержимое раздела.
    /// </summary>
    public string Content { get; set; } =
        string.Empty;

    /// <summary>
    /// Получает или задаёт сохранённый вектор эмбеддинга.
    /// </summary>
    public float[] Embedding { get; set; } =
        Array.Empty<float>();
}
