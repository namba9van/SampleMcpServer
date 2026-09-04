#pragma warning disable KMEXP00
#pragma warning disable SKEXP0001
using Microsoft.Extensions.VectorData;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
/// <summary>
/// Предоставляет реализацию векторного хранилища в памяти на основе одного FAISS-индекса.
/// </summary>
public class FaissVectorStore : VectorStore
{
    private readonly IEmbeddingGenerator? _embeddingGenerator;
    private readonly ConcurrentDictionary<string, object> _collections;
    /// <summary>
    /// Создаёт хранилище с указанным генератором эмбеддингов для строковых поисковых запросов.
    /// </summary>
    /// <param name="embeddingGenerator">Генератор, используемый для преобразования текста поискового запроса в векторы.</param>
    public FaissVectorStore(IEmbeddingGenerator? embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
        _collections = new ConcurrentDictionary<string, object>();
    }
    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_collections.ContainsKey(name));
    }
    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        _collections.TryRemove(name, out _);
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
    {
        var collection = new FaissVectorStoreCollection<TKey, TRecord>(name, _embeddingGenerator, definition);
        _collections[name] = collection;
        return collection;
    }
    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
    {
        var collection = new FaissVectorStoreCollection<object, Dictionary<string, object?>>(name, _embeddingGenerator, definition);
        _collections[name] = collection;
        return collection;
    }
    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }
    /// <inheritdoc />
    public override IAsyncEnumerable<string> ListCollectionNamesAsync(CancellationToken cancellationToken = default)
    {
        return _collections.Keys.ToAsyncEnumerable();
    }
}
/// <summary>
/// Представляет именованную коллекцию на основе FAISS и соответствие записей векторным идентификаторам.
/// </summary>
internal class FaissVectorStoreCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly string _name;
    private readonly IEmbeddingGenerator? _embeddingGenerator;
    private readonly VectorStoreCollectionDefinition? _definition;
    private readonly ConcurrentDictionary<TKey, TRecord> _records;
    private FaissNet.Index? _index;
    private readonly Dictionary<TKey, long> _keyToIndexMap;
    private readonly List<TKey> _indexToKeyMap;
    private int _dimension;
    /// <summary>
    /// Создаёт коллекцию с указанным именем и определением векторного хранилища.
    /// </summary>
    /// <param name="name">Логическое имя коллекции.</param>
    /// <param name="embeddingGenerator">Необязательный генератор, используемый для строкового поиска.</param>
    /// <param name="definition">Необязательное определение коллекции векторного хранилища.</param>
    public FaissVectorStoreCollection(string name, IEmbeddingGenerator? embeddingGenerator, VectorStoreCollectionDefinition? definition)
    {
        _name = name;
        _embeddingGenerator = embeddingGenerator;
        _definition = definition;
        _records = new ConcurrentDictionary<TKey, TRecord>();
        _keyToIndexMap = new Dictionary<TKey, long>();
        _indexToKeyMap = new List<TKey>();
        _dimension = 0;
    }
    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
    /// <inheritdoc />
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(key, out _);
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        _records.Clear();
        _index?.Dispose();
        _index = null;
        _keyToIndexMap.Clear();
        _indexToKeyMap.Clear();
        _dimension = 0;
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(key, out TRecord? record);
        return Task.FromResult(record);
    }
    /// <inheritdoc />
    public override IAsyncEnumerable<TRecord> GetAsync(Expression<Func<TRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>? options = null, CancellationToken cancellationToken = default)
    {
        var compiledFilter = filter.Compile();
        return _records.Values.Where(compiledFilter).Take(top).ToAsyncEnumerable();
    }
    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }
    /// <summary>
    /// Выполняет поиск по индексу FAISS и возвращает записи с наибольшей оценкой сходства.
    /// </summary>
    /// <typeparam name="TInput">Тип переданного значения поискового запроса.</typeparam>
    /// <param name="searchValue">Вектор или значение, которое можно преобразовать в текст для генерации эмбеддинга.</param>
    /// <param name="top">Максимальное количество соседей для возврата.</param>
    /// <param name="options">Необязательные параметры векторного поиска.</param>
    /// <param name="cancellationToken">Токен отмены для асинхронного перечисления.</param>
    /// <returns>Асинхронная последовательность результатов векторного поиска, отсортированных по оценке FAISS.</returns>
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(TInput searchValue, int top, VectorSearchOptions<TRecord>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<float> searchVector;
        if (_embeddingGenerator != null && searchValue is not ReadOnlyMemory<float>)
        {
            if (typeof(TInput) == typeof(string) && _embeddingGenerator is IEmbeddingGenerator<string, Embedding<float>> stringGenerator)
            {
                var stringInput = (string)(object)searchValue;
                searchVector = await stringGenerator.GenerateVectorAsync(stringInput, cancellationToken: cancellationToken);
            }
            else
            {
                var stringInput = searchValue?.ToString() ?? "";
                if (_embeddingGenerator is IEmbeddingGenerator<string, Embedding<float>> stringGen)
                {
                    searchVector = await stringGen.GenerateVectorAsync(stringInput, cancellationToken: cancellationToken);
                }
                else
                {
                    throw new NotSupportedException($"Embedding generator type {_embeddingGenerator.GetType()} is not supported for input type {typeof(TInput)}.");
                }
            }
        }
        else if (searchValue is ReadOnlyMemory<float> readOnlyMemory)
        {
            searchVector = readOnlyMemory;
        }
        else
        {
            throw new NotSupportedException($"Search value of type {typeof(TInput)} is not supported.");
        }

        if (_index != null && _records.Count > 0)
        {
            var searchResult = _index.Search(new float[][] { searchVector.ToArray() }, top);
            var nbrDists = searchResult.Item1[0];
            var nbrIds = searchResult.Item2[0];

            for (int i = 0; i < nbrIds.Length && i < top; i++)
            {
                var faissId = nbrIds[i];
                var keyEntry = _keyToIndexMap.FirstOrDefault(kv => kv.Value == faissId);
                if (keyEntry.Key != null)
                {
                    var key = keyEntry.Key;
                    if (_records.TryGetValue(key, out TRecord? record))
                    {
                        yield return new VectorSearchResult<TRecord>(record, nbrDists[i]);
                    }
                }
            }
        }
    }
    /// <inheritdoc />
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        var key = ExtractKeyFromRecord(record);
        var vector = ExtractVectorFromRecord(record);

        _records[key] = record;

        if (vector.HasValue)
        {
            UpdateFaissIndex(key, vector.Value);
        }

        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            UpsertAsync(record, cancellationToken);
        }
        return Task.CompletedTask;
    }
    /// <summary>
    /// Извлекает ключ векторного хранилища из динамического словаря либо из свойства с атрибутом.
    /// </summary>
    /// <param name="record">Запись, для которой требуется ключ.</param>
    /// <returns>Ключ, связанный с записью.</returns>
    /// <exception cref="InvalidOperationException">Возникает, если совместимый ключ не найден.</exception>
    private TKey ExtractKeyFromRecord(TRecord record)
    {
        if (record is IDictionary<string, object?> dictRecord)
        {
            if (dictRecord.TryGetValue("id", out object? idValue) && idValue is TKey idTKey)
                return idTKey;
            if (dictRecord.TryGetValue("key", out object? keyValue) && keyValue is TKey keyTKey)
                return keyTKey;
            if (dictRecord.TryGetValue("Key", out object? key2Value) && key2Value is TKey key2TKey)
                return key2TKey;
        }
        else if (record != null)
        {
            var keyProperty = typeof(TRecord).GetProperties()
                .FirstOrDefault(p => p.GetCustomAttributes(typeof(VectorStoreKeyAttribute), false).Length > 0);

            if (keyProperty != null)
            {
                var value = keyProperty.GetValue(record);
                if (value is TKey key)
                    return key;
            }
        }

        throw new InvalidOperationException($"Could not extract key from record of type {typeof(TRecord)}");
    }
    /// <summary>
    /// Извлекает вектор эмбеддинга из динамического словаря либо из свойства с атрибутом.
    /// </summary>
    /// <param name="record">Запись, для которой требуется вектор.</param>
    /// <returns>Сохранённый вектор или <see langword="null"/>, если вектор отсутствует.</returns>
    private ReadOnlyMemory<float>? ExtractVectorFromRecord(TRecord record)
    {
        if (record is IDictionary<string, object?> dictRecord)
        {
            if (dictRecord.TryGetValue("embedding", out object? embeddingValue))
            {
                if (embeddingValue is ReadOnlyMemory<float> embeddingMemory)
                    return embeddingMemory;
                if (embeddingValue is float[] embeddingArray)
                    return new ReadOnlyMemory<float>(embeddingArray);
            }
            if (dictRecord.TryGetValue("vector", out object? vectorValue))
            {
                if (vectorValue is ReadOnlyMemory<float> vectorMemory)
                    return vectorMemory;
                if (vectorValue is float[] vectorArray)
                    return new ReadOnlyMemory<float>(vectorArray);
            }
            if (dictRecord.TryGetValue("Embedding", out object? embedding2Value))
            {
                if (embedding2Value is ReadOnlyMemory<float> embeddingMemory)
                    return embeddingMemory;
                if (embedding2Value is float[] embeddingArray)
                    return new ReadOnlyMemory<float>(embeddingArray);
            }
        }
        else if (record != null)
        {
            var vectorProperty = typeof(TRecord).GetProperties()
                .FirstOrDefault(p => p.GetCustomAttributes(typeof(VectorStoreVectorAttribute), false).Length > 0);

            if (vectorProperty != null)
            {
                var value = vectorProperty.GetValue(record);
                if (value is ReadOnlyMemory<float> readOnlyMemory)
                    return readOnlyMemory;
                if (value is float[] floatArray)
                    return new ReadOnlyMemory<float>(floatArray);
            }
        }

        return null;
    }
    /// <summary>
    /// Добавляет вектор записи в индекс FAISS и проверяет его размерность.
    /// </summary>
    /// <param name="key">Ключ записи, используемый для сопоставления идентификатора FAISS обратно с записью.</param>
    /// <param name="vector">Вектор, добавляемый в индекс.</param>
    /// <exception cref="InvalidOperationException">Возникает, когда размерность вектора конфликтует с уже существующим индексом.</exception>
    /// <remarks>
    /// ВАЖНОЕ ОГРАНИЧЕНИЕ: настоящего обновления вектора "на месте" для уже существующего
    /// ключа здесь нет. Базовый индекс FAISS создаётся с типом "IDMap,HNSW32" — а FAISS не
    /// поддерживает remove_ids для HNSW-индексов, поэтому старый вектор физически убрать из
    /// индекса нельзя без полной пересборки. При повторном upsert уже известного ключа новый
    /// вектор добавляется под новым внутренним id, а старый id становится осиротевшим:
    /// он остаётся в самом FAISS-индексе (занимает место и участвует в поиске соседей), но
    /// больше не резолвится обратно в запись, поскольку _keyToIndexMap[key] уже указывает на
    /// новый id — SearchAsync корректно пропускает такие осиротевшие совпадения, не возвращая
    /// их вызывающему, но они всё равно отнимают "слот" из top-k результатов поиска.
    /// В текущем использовании (RagIndexService) это не проявляется: коллекция полностью
    /// пересоздаётся заново при каждой переиндексации, поэтому повторный upsert одного и
    /// того же ключа в рамках одной коллекции не происходит. Но при прямом переиспользовании
    /// этого класса с долгоживущей коллекцией и обновлением одних и тех же ключей юзер
    /// получит постепенно "разбухающий" индекс и деградацию качества поиска.
    /// </remarks>
    private void UpdateFaissIndex(TKey key, ReadOnlyMemory<float> vector)
    {
        if (_dimension == 0)
        {
            _dimension = vector.Length;
        }
        else if (_dimension != vector.Length)
        {
            throw new InvalidOperationException($"Vector dimension mismatch. Expected: {_dimension}, Got: {vector.Length}");
        }

        if (_index == null)
        {
            _index = FaissNet.Index.Create(_dimension, "IDMap,HNSW32", FaissNet.MetricType.METRIC_INNER_PRODUCT);
        }

        // См. <remarks> выше: ветки "ключ уже существует" и "новый ключ" сейчас идентичны —
        // настоящей замены вектора по существующему id не производится ни в одном случае.
        var newIndexId = (long)_indexToKeyMap.Count;
        _keyToIndexMap[key] = newIndexId;
        _indexToKeyMap.Add(key);
        var vectorArray = vector.ToArray();
        _index.AddWithIds(new float[][] { vectorArray }, new long[] { newIndexId });
    }
}
