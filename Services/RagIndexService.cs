using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Services;
/// <summary>
/// Maintains the persistent RAG index and performs similarity searches over its documents.
/// </summary>
/// <summary>
/// Maintains the persistent RAG index and performs similarity searches over its documents.
/// </summary>
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
    /// <summary>
    /// Initializes the persistent RAG index service.
    /// </summary>
    /// <param name="embeddingService">The embedding generator service.</param>
    /// <param name="modelDiscovery">The embedding-model discovery service.</param>
    /// <param name="documentLoader">The document loading service.</param>
    public RagIndexService(
        EmbeddingService embeddingService,
        LmStudioModelDiscovery modelDiscovery,
        RagDocumentLoader documentLoader)
    {
        _embeddingService = embeddingService;
        _modelDiscovery = modelDiscovery;
        _documentLoader = documentLoader;
    }
    /// <summary>
    /// Synchronizes the persistent RAG index with the requested files and rebuilds the in-memory FAISS index.
    /// </summary>
    /// <param name="path">A file or directory whose contents should be represented in the RAG index.</param>
    /// <param name="cancellationToken">The cancellation token for indexing.</param>
    /// <exception cref="InvalidOperationException">Thrown when stored embeddings are incompatible with the current model or dimension.</exception>
    public async Task EnsureIndexUpToDateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
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

            var existingMetadata =
                await LoadMetadataAsync(
                    metadataPath,
                    cancellationToken);

            var documentsAreAvailable =
                File.Exists(documentsPath);

            var documents =
                existingMetadata is null ||
                !documentsAreAvailable
                    ? new List<RagDocument>()
                    : await LoadDocumentsAsync(
                        documentsPath,
                        cancellationToken);

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

            var requestedFiles =
                _documentLoader
                    .GetFiles(path)
                    .Select(Path.GetFullPath)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

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

            var documentMap =
                documents.ToDictionary(
                    x => x.Id,
                    StringComparer.OrdinalIgnoreCase);

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

            foreach (var changedFile in changedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.Error.WriteLine(
                    $"Indexing changed file: {changedFile}");

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

                var loaded =
                    await _documentLoader.LoadAsync(
                        changedFile,
                        cancellationToken);

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

                existingMetadata.Files[
                    changedFile] =
                    fileMetadata;
            }

            documents =
                documentMap.Values
                    .OrderBy(
                        x => x.FileName)
                    .ThenBy(
                        x => x.Id)
                    .ToList();

            if (existingMetadata.EmbeddingDimension == 0 &&
                documents.Count > 0)
            {
                existingMetadata.EmbeddingDimension =
                    documents[0]
                        .Embedding
                        .Length;
            }

            existingMetadata.EmbeddingModel =
                model;

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

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _collection
                    .UpsertAsync(
                        document,
                        cancellationToken);
            }

            _metadata =
                existingMetadata;

            await SaveDocumentsAsync(
                documentsPath,
                documents,
                cancellationToken);

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
    /// <summary>
    /// Searches the current in-memory RAG index and filters results by similarity threshold.
    /// </summary>
    /// <param name="query">The natural-language search query.</param>
    /// <param name="limit">The maximum number of candidates requested from FAISS.</param>
    /// <param name="threshold">The minimum result score to keep.</param>
    /// <param name="cancellationToken">The cancellation token for the search.</param>
    /// <returns>The matching vector-search results ordered from highest to lowest score.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the index has not been initialized.</exception>
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

    /// <summary>
    /// Loads persistent RAG metadata from disk.
    /// </summary>
    /// <param name="path">The metadata file path.</param>
    /// <param name="cancellationToken">The cancellation token for deserialization.</param>
    /// <returns>The metadata, or <see langword="null"/> when it is missing or invalid.</returns>
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

    /// <summary>
    /// Loads persisted RAG documents and their embeddings from disk.
    /// </summary>
    /// <param name="path">The documents file path.</param>
    /// <param name="cancellationToken">The cancellation token for deserialization.</param>
    /// <returns>The restored documents, or an empty list when the file is missing or invalid.</returns>
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

    /// <summary>
    /// Persists RAG documents and embeddings using a temporary file before replacement.
    /// </summary>
    /// <param name="path">The destination documents file.</param>
    /// <param name="documents">The documents to persist.</param>
    /// <param name="cancellationToken">The cancellation token for serialization.</param>
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

    /// <summary>
    /// Persists RAG index metadata using a temporary file before replacement.
    /// </summary>
    /// <param name="path">The destination metadata file.</param>
    /// <param name="metadata">The metadata to persist.</param>
    /// <param name="cancellationToken">The cancellation token for serialization.</param>
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

    /// <summary>
    /// Returns the per-user directory used to store the persistent RAG index.
    /// </summary>
    /// <returns>The absolute path of the RAG index directory.</returns>
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

    /// <summary>
    /// Replaces a destination file with a temporary file.
    /// </summary>
    /// <param name="tempPath">The temporary file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    private static void ReplaceFile(
        string tempPath,
        string destinationPath)
    {
        File.Move(
            tempPath,
            destinationPath,
            true);
    }

    /// <summary>
    /// Represents the serialized form of a RAG document.
    /// </summary>
    private sealed class RagStoredDocument
    {
        /// <summary>
        /// Gets or sets the persisted document identifier.
        /// </summary>
        public string Id { get; set; } =
            string.Empty;

        /// <summary>
        /// Gets or sets the persisted source file path.
        /// </summary>
        public string FileName { get; set; } =
            string.Empty;

        /// <summary>
        /// Gets or sets the persisted document content.
        /// </summary>
        public string Content { get; set; } =
            string.Empty;

        /// <summary>
        /// Gets or sets the persisted embedding vector.
        /// </summary>
        public float[] Embedding { get; set; } =
            Array.Empty<float>();
    }
}
