using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Services;

public sealed class RagIndexService
{
    private readonly EmbeddingService _embeddingService;

    private readonly LmStudioModelDiscovery _modelDiscovery;

    private readonly RagDocumentLoader _documentLoader;

    private readonly SemaphoreSlim _lock =
        new(1, 1);

    private FaissVectorStore? _vectorStore;

    private Microsoft.Extensions.VectorData.VectorStoreCollection<
        string,
        RagDocument>? _collection;

    private RagIndexMetadata? _metadata;

    private string? _indexRoot;

    public RagIndexService(
        EmbeddingService embeddingService,
        LmStudioModelDiscovery modelDiscovery,
        RagDocumentLoader documentLoader)
    {
        _embeddingService = embeddingService;
        _modelDiscovery = modelDiscovery;
        _documentLoader = documentLoader;
    }

    public async Task EnsureIndexUpToDateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            /*
             * 1. Получаем embedding generator и текущую модель.
             *
             * EmbeddingService / ModelDiscovery уже выполняют
             * автоматическое обнаружение LM Studio и embedding-модели.
             */
			var embeddingGenerator =
				await _embeddingService
					.GetGeneratorAsync(
						null,
						null,
						cancellationToken);

            var model =
                await _modelDiscovery
                    .GetEmbeddingModelAsync(
                        cancellationToken);

            /*
             * 2. Определяем расположение persistent RAG index.
             */
            var indexRoot =
                GetIndexRoot();

            Directory.CreateDirectory(
                indexRoot);

            _indexRoot =
                indexRoot;

            var metadataPath =
                Path.Combine(
                    indexRoot,
                    "metadata.json");

            var documentsPath =
                Path.Combine(
                    indexRoot,
                    "documents.json");

            /*
             * 3. Загружаем metadata.
             */
            var existingMetadata =
                await LoadMetadataAsync(
                    metadataPath,
                    cancellationToken);

            /*
             * 4. Загружаем сохранённые документы/embeddings.
             *
             * Если documents.json отсутствует или повреждён,
             * считаем индекс недействительным.
             */
            var documentsAreAvailable =
                File.Exists(documentsPath);

            var documents =
                existingMetadata is null ||
                !documentsAreAvailable
                    ? new List<RagDocument>()
                    : await LoadDocumentsAsync(
                        documentsPath,
                        cancellationToken);

            /*
             * Если metadata существует, но documents.json пустой
             * или не удалось его прочитать, индекс нельзя считать
             * актуальным.
             */
            if (existingMetadata is not null &&
                documentsAreAvailable &&
                documents.Count == 0 &&
                existingMetadata.DocumentCount > 0)
            {
                Console.Error.WriteLine(
                    "RAG documents are missing or invalid. " +
                    "Rebuilding index.");

                existingMetadata = null;
                documents.Clear();
            }

            /*
             * 5. Проверяем совместимость embedding-модели.
             */
            if (existingMetadata is not null &&
                !string.Equals(
                    existingMetadata.EmbeddingModel,
                    model,
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    "Embedding model changed. " +
                    "Rebuilding RAG index.");

                existingMetadata = null;
                documents.Clear();
            }

            /*
             * 6. Проверяем размерность сохранённых embeddings.
             */
            if (existingMetadata is not null &&
                existingMetadata.EmbeddingDimension > 0 &&
                documents.Count > 0)
            {
                var actualDimension =
                    documents[0]
                        .Embedding
                        .Length;

                if (actualDimension !=
                    existingMetadata.EmbeddingDimension)
                {
                    Console.Error.WriteLine(
                        "Embedding dimension changed. " +
                        "Rebuilding RAG index.");

                    existingMetadata = null;
                    documents.Clear();
                }
            }

            /*
             * 7. Получаем список файлов, относящихся
             * к текущему RAG-запросу.
             */
            var requestedFiles =
                _documentLoader
                    .GetFiles(path)
                    .Select(Path.GetFullPath)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            /*
             * 8. Если индекс отсутствует или признан
             * несовместимым — начинаем новый индекс.
             */
            if (existingMetadata is null)
            {
                existingMetadata =
                    new RagIndexMetadata
                    {
                        EmbeddingModel =
                            model,

                        EmbeddingDimension = 0,

                        Files =
                            new Dictionary<
                                string,
                                RagFileMetadata>(
                                StringComparer
                                    .OrdinalIgnoreCase)
                    };

                documents.Clear();
            }

            /*
             * 9. Создаём map документов:
             *
             * document ID -> document
             */
            var documentMap =
                documents.ToDictionary(
                    x => x.Id,
                    StringComparer.OrdinalIgnoreCase);

            /*
             * 10. Группируем документы по исходному файлу.
             */
            var fileGroups =
                documents
                    .GroupBy(
                        x => Path.GetFullPath(
                            x.FileName ??
                            string.Empty),
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToList(),
                        StringComparer.OrdinalIgnoreCase);

            /*
             * 11. Определяем изменившиеся файлы.
             */
            var changedFiles =
                new List<string>();

            foreach (var file in requestedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fingerprint =
                    RagFileFingerprint.Create(file);

                if (!existingMetadata.Files.TryGetValue(
                        file,
                        out var fileMetadata) ||
                    !string.Equals(
                        fileMetadata.Fingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    changedFiles.Add(file);
                }
            }

            /*
             * 12. Определяем удалённые файлы.
             */
            var requestedFileSet =
                new HashSet<string>(
                    requestedFiles,
                    StringComparer.OrdinalIgnoreCase);

            var deletedFiles =
                existingMetadata.Files.Keys
                    .Where(file =>
                        !requestedFileSet.Contains(file))
                    .ToList();

            Console.Error.WriteLine(
                $"RAG files: {requestedFiles.Count}");

            Console.Error.WriteLine(
                $"RAG changed files: {changedFiles.Count}");

            Console.Error.WriteLine(
                $"RAG deleted files: {deletedFiles.Count}");

            /*
             * 13. Удаляем из metadata и documentMap
             * документы файлов, которых больше нет
             * в текущем наборе.
             */
            foreach (var deletedFile in deletedFiles)
            {
                existingMetadata.Files.Remove(
                    deletedFile);

                if (fileGroups.TryGetValue(
                        deletedFile,
                        out var oldDocuments))
                {
                    foreach (var oldDocument in oldDocuments)
                    {
                        documentMap.Remove(
                            oldDocument.Id);
                    }
                }
            }

            /*
             * 14. Переиндексируем только изменившиеся файлы.
             */
            foreach (var changedFile in changedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.Error.WriteLine(
                    $"Indexing changed file: {changedFile}");

                /*
                 * Удаляем старые chunks этого файла.
                 */
                if (fileGroups.TryGetValue(
                        changedFile,
                        out var oldDocuments))
                {
                    foreach (var oldDocument in oldDocuments)
                    {
                        documentMap.Remove(
                            oldDocument.Id);
                    }
                }

                /*
                 * Загружаем содержимое документа.
                 */
                var loaded =
                    await _documentLoader.LoadAsync(
                        changedFile,
                        cancellationToken);

                /*
                 * Новый fingerprint.
                 */
                var fileFingerprint =
                    RagFileFingerprint.Create(
                        changedFile);

                var fileMetadata =
                    new RagFileMetadata
                    {
                        FilePath =
                            changedFile,

                        Fingerprint =
                            fileFingerprint,

                        Sections =
                            new List<
                                RagSectionMetadata>()
                    };

                /*
                 * Генерируем embeddings только для изменившегося файла.
                 */
                foreach (var document in loaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(
                            document.Content))
                    {
                        continue;
                    }

                    var embedding =
                        await embeddingGenerator
                            .GenerateVectorAsync(
                                document.Content,
                                cancellationToken:
                                    cancellationToken);

                    document.Embedding =
                        embedding;

                    documentMap[
                        document.Id] =
                        document;

                    fileMetadata.Sections.Add(
                        new RagSectionMetadata
                        {
                            Id =
                                document.Id,

                            Content =
                                document.Content,

                            Embedding =
                                embedding.ToArray()
                        });

                    /*
                     * Для нового индекса dimension
                     * определяем по фактическому embedding.
                     */
                    if (existingMetadata
                            .EmbeddingDimension == 0)
                    {
                        existingMetadata
                            .EmbeddingDimension =
                            embedding.Length;
                    }
                    else if (existingMetadata
                                 .EmbeddingDimension !=
                             embedding.Length)
                    {
                        throw new InvalidOperationException(
                            "Размерность embedding изменилась " +
                            "внутри одного индекса. " +
                            $"Ожидалось: " +
                            $"{existingMetadata.EmbeddingDimension}, " +
                            $"получено: {embedding.Length}.");
                    }
                }

                /*
                 * Сохраняем fingerprint файла.
                 */
                existingMetadata.Files[
                    changedFile] =
                    fileMetadata;
            }

            /*
             * 15. Формируем окончательный набор документов.
             */
            documents =
                documentMap.Values
                    .OrderBy(
                        x => x.FileName)
                    .ThenBy(
                        x => x.Id)
                    .ToList();

            /*
             * 16. Если dimension ещё неизвестна,
             * пытаемся получить её из сохранённых документов.
             */
            if (existingMetadata.EmbeddingDimension == 0 &&
                documents.Count > 0)
            {
                existingMetadata.EmbeddingDimension =
                    documents[0]
                        .Embedding
                        .Length;
            }

            /*
             * 17. Обновляем metadata текущей моделью.
             */
            existingMetadata.EmbeddingModel =
                model;

            /*
             * 18. Пересоздаём in-memory FAISS
             * из уже сохранённых embeddings.
             *
             * Важно:
             * здесь НЕ вызывается GenerateVectorAsync()
             * для неизменённых документов.
             */
            _vectorStore =
                new FaissVectorStore(
                    embeddingGenerator);

            _collection =
                _vectorStore.GetCollection<
                    string,
                    RagDocument>(
                        "infos");

            await _collection
                .EnsureCollectionExistsAsync()
                .ConfigureAwait(false);

            /*
             * 19. Восстанавливаем FAISS.
             */
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _collection
                    .UpsertAsync(
                        document,
                        cancellationToken);
            }

            /*
             * 20. Сохраняем состояние в памяти.
             */
            _metadata =
                existingMetadata;

            /*
             * 21. Сохраняем embeddings на диск.
             */
            await SaveDocumentsAsync(
                documentsPath,
                documents,
                cancellationToken);

            /*
             * 22. Сохраняем metadata на диск.
             */
            await SaveMetadataAsync(
                metadataPath,
                existingMetadata,
                cancellationToken);

            Console.Error.WriteLine(
                $"RAG index ready. " +
                $"Documents={documents.Count}, " +
                $"Dimension=" +
                $"{existingMetadata.EmbeddingDimension}, " +
                $"Model=" +
                $"{existingMetadata.EmbeddingModel}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<
        List<
            Microsoft.Extensions.VectorData
                .VectorSearchResult<RagDocument>>>
        SearchAsync(
            string query,
            int limit,
            double threshold,
            CancellationToken cancellationToken = default)
    {
        if (_collection is null)
        {
            throw new InvalidOperationException(
                "RAG index ещё не инициализирован.");
        }

        var searchResults =
            await _collection
                .SearchAsync(
                    query,
                    top: limit,
                    cancellationToken:
                        cancellationToken)
                .Where(x =>
                    x.Score >= threshold)
                .OrderByDescending(
                    x => x.Score)
                .ToListAsync(
                    cancellationToken);

        return searchResults;
    }

    private async Task<RagIndexMetadata?>
        LoadMetadataAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream =
                File.OpenRead(path);

            return await JsonSerializer
                .DeserializeAsync<
                    RagIndexMetadata>(
                        stream,
                        cancellationToken:
                            cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Не удалось загрузить RAG metadata: " +
                $"{ex.Message}");

            return null;
        }
    }

    private async Task<List<RagDocument>>
        LoadDocumentsAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new List<RagDocument>();
        }

        try
        {
            await using var stream =
                File.OpenRead(path);

            var stored =
                await JsonSerializer
                    .DeserializeAsync<
                        List<RagStoredDocument>>(
                        stream,
                        cancellationToken:
                            cancellationToken)
                ?? new List<RagStoredDocument>();

            return stored
                .Select(x =>
                    new RagDocument
                    {
                        Id =
                            x.Id,

                        FileName =
                            x.FileName,

                        Content =
                            x.Content,

                        Embedding =
                            new ReadOnlyMemory<float>(
                                x.Embedding)
                    })
                .ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Не удалось загрузить RAG documents: " +
                $"{ex.Message}");

            return new List<RagDocument>();
        }
    }

    private async Task SaveDocumentsAsync(
        string path,
        List<RagDocument> documents,
        CancellationToken cancellationToken)
    {
        var tempPath =
            path + ".tmp";

        var stored =
            documents
                .Select(x =>
                    new RagStoredDocument
                    {
                        Id =
                            x.Id,

                        FileName =
                            x.FileName ??
                            string.Empty,

                        Content =
                            x.Content ??
                            string.Empty,

                        Embedding =
                            x.Embedding.ToArray()
                    })
                .ToList();

        await using (
            var stream =
                File.Create(tempPath))
        {
            await JsonSerializer
                .SerializeAsync(
                    stream,
                    stored,
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    },
                    cancellationToken);
        }

        ReplaceFile(
            tempPath,
            path);
    }

    private async Task SaveMetadataAsync(
        string path,
        RagIndexMetadata metadata,
        CancellationToken cancellationToken)
    {
        var tempPath =
            path + ".tmp";

        await using (
            var stream =
                File.Create(tempPath))
        {
            await JsonSerializer
                .SerializeAsync(
                    stream,
                    metadata,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    },
                    cancellationToken);
        }

        ReplaceFile(
            tempPath,
            path);
    }

    private static string GetIndexRoot()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            localAppData,
            "SampleMcpServer",
            "RagIndex");
    }

    private static void ReplaceFile(
        string tempPath,
        string destinationPath)
    {
        File.Move(
            tempPath,
            destinationPath,
            true);
    }

    private sealed class RagStoredDocument
    {
        public string Id { get; set; } =
            string.Empty;

        public string FileName { get; set; } =
            string.Empty;

        public string Content { get; set; } =
            string.Empty;

        public float[] Embedding { get; set; } =
            Array.Empty<float>();
    }
}