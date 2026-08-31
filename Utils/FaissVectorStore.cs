#pragma warning disable KMEXP00
#pragma warning disable SKEXP0001
using Microsoft.Extensions.VectorData;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
/// <summary>
/// Предоставляет один в памяти векторного хранилища реализация на основе по один FAISS индекс.
/// </summary>
public class FaissVectorStore : VectorStore
{
    private readonly IEmbeddingGenerator? _embeddingGenerator;
    private readonly ConcurrentDictionary<string, object> _collections;
    /// <summary>
    /// Описывает назначение элемента.
    /// </summary>
    /// <param name="embeddingGenerator">генератор используемый для преобразовать текст поиск values в vectors.</param>
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
    /// Создаёт один коллекция с переданное имя и векторного хранилища определение.
    /// </summary>
    /// <param name="name">logical коллекция имя.</param>
    /// <param name="embeddingGenerator">необязательный генератор используемый для строковый поиск.</param>
    /// <param name="definition">необязательный векторного хранилища коллекция определение.</param>
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
    /// Выполняет поиск FAISS индекс и yields с наибольшей оценкой записи.
    /// </summary>
    /// <typeparam name="TInput">type из переданное поиск значение.</typeparam>
    /// <param name="searchValue">вектор или один значение that может быть преобразованный для текст для embedding generation.</param>
    /// <param name="top">максимальный число из neighbors для возврат.</param>
    /// <param name="options">Необязательные параметры векторного поиска.</param>
    /// <param name="cancellationToken">отмена токен для асинхронный перечисление.</param>
    /// <returns>асинхронный последовательность из vector-search результаты отсортированные по FAISS score.</returns>
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
    /// извлекает векторного хранилища ключ из один динамический словаря или один с атрибутом свойство.
    /// </summary>
    /// <param name="record">запись для которого ключ является требуемый.</param>
    /// <returns>ключ связанный с запись.</returns>
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
    /// извлекает один embedding вектор из один динамический словаря или один с атрибутом свойство.
    /// </summary>
    /// <param name="record">запись для которого вектор является требуемый.</param>
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
    /// <param name="key">запись ключ используемый для соответствие FAISS идентификатор обратно для запись.</param>
    /// <param name="vector">вектор для добавить для индекс.</param>
    /// <exception cref="InvalidOperationException">возникает когда вектор размерность конфликтует с существующим индекс.</exception>
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

        if (_keyToIndexMap.TryGetValue(key, out long existingIndex))
        {
            var newIndexId = (long)_indexToKeyMap.Count;
            _keyToIndexMap[key] = newIndexId;
            _indexToKeyMap.Add(key);
            var vectorArray = vector.ToArray();
            _index.AddWithIds(new float[][] { vectorArray }, new long[] { newIndexId });
        }
        else
        {
            var newIndexId = (long)_indexToKeyMap.Count;
            _keyToIndexMap[key] = newIndexId;
            _indexToKeyMap.Add(key);
            var vectorArray = vector.ToArray();
            _index.AddWithIds(new float[][] { vectorArray }, new long[] { newIndexId });
        }
    }
}
