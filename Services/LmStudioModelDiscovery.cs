using System.Net.Http.Headers;
using System.Text.Json;

namespace Services;
/// <summary>
/// Определяет доступную в LM Studio embedding-модель.
/// </summary>
public sealed class LmStudioModelDiscovery
{
    private readonly LmStudioEndpoint _endpoint;
    private readonly HttpClient _httpClient;

    private readonly string? _configuredModel;

    private string? _cachedModel;
    /// <summary>
    /// Инициализирует обнаружение модели и читает необязательное переопределение модели.
    /// </summary>
    /// <param name="endpoint">Определитель endpoint LM Studio.</param>
    public LmStudioModelDiscovery(
        LmStudioEndpoint endpoint)
    {
        _endpoint = endpoint;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        _configuredModel =
            Environment.GetEnvironmentVariable("EMBEDD_MODEL");
    }
    /// <summary>
    /// Выбирает embedding-модель с использованием явного переопределения, автоматического обнаружения или списка загруженных моделей.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для запросов обнаружения модели.</param>
    /// <returns>Идентификатор выбранной embedding-модели.</returns>
    /// <exception cref="InvalidOperationException">Возникает, когда embedding-модель не может быть выбрана однозначно.</exception>
    public async Task<string> GetEmbeddingModelAsync(
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_configuredModel))
        {
            return _configuredModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_cachedModel))
        {
            return _cachedModel;
        }

        var endpoint =
            await _endpoint.GetAsync(cancellationToken);

        var nativeEndpoint =
            GetNativeApiEndpoint(endpoint);

        var models =
            await GetModelsAsync(
                nativeEndpoint,
                cancellationToken);

        var embeddingModels =
            models
                .Where(x =>
                    string.Equals(
                        x.Type,
                        "embedding",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (embeddingModels.Count == 0)
        {
            throw new InvalidOperationException(
                "В LM Studio не найдена ни одна embedding-модель.");
        }

        if (embeddingModels.Count == 1)
        {
            _cachedModel =
                embeddingModels[0].Key;

            LogSelectedModel(
                embeddingModels[0]);

            return _cachedModel;
        }

        var loadedModel =
            await FindLoadedEmbeddingModelAsync(
                endpoint,
                embeddingModels,
                cancellationToken);

        if (loadedModel is not null)
        {
            _cachedModel =
                loadedModel.Key;

            LogSelectedModel(
                loadedModel);

            return _cachedModel;
        }

        var availableModels =
            string.Join(
                Environment.NewLine,
                embeddingModels.Select(x =>
                    $"  - {x.Key} ({x.DisplayName})"));

        throw new InvalidOperationException(
            "В LM Studio обнаружено несколько embedding-моделей, " +
            "но невозможно однозначно определить, какую использовать."
            + Environment.NewLine
            + "Доступные embedding-модели:"
            + Environment.NewLine
            + availableModels
            + Environment.NewLine
            + Environment.NewLine
            + "Задайте EMBEDD_MODEL вручную.");
    }
    /// <summary>
    /// Читает собственный каталог моделей LM Studio.
    /// </summary>
    /// <param name="nativeEndpoint">Собственный API-endpoint LM Studio.</param>
    /// <param name="cancellationToken">Токен отмены для запроса.</param>
    /// <returns>Модели, возвращённые LM Studio.</returns>
    private async Task<List<LmStudioModelInfo>>
        GetModelsAsync(
            string nativeEndpoint,
            CancellationToken cancellationToken)
    {
        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{nativeEndpoint}/models");

            AddAuthorization(request);

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"LM Studio /api/v1/models вернул " +
                    $"{(int)response.StatusCode} " +
                    $"{response.StatusCode}.");
            }

            var json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            using var document =
                JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(
                    "models",
                    out var modelsElement))
            {
                throw new InvalidOperationException(
                    "Ответ LM Studio не содержит поля 'models'.");
            }

            var result =
                new List<LmStudioModelInfo>();

            foreach (var modelElement
                     in modelsElement.EnumerateArray())
            {
                var key =
                    GetStringProperty(
                        modelElement,
                        "key");

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var type =
                    GetStringProperty(
                        modelElement,
                        "type");

                var displayName =
                    GetStringProperty(
                        modelElement,
                        "display_name")
                    ?? key;

                result.Add(
                    new LmStudioModelInfo
                    {
                        Key = key,
                        Type = type ?? string.Empty,
                        DisplayName = displayName
                    });
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Не удалось получить список моделей LM Studio.",
                ex);
        }
    }

    /// <summary>
    /// Находит embedding-модель, указанную как загруженную через совместимый с OpenAI endpoint моделей.
    /// </summary>
    /// <param name="endpoint">Совместимый с OpenAI endpoint LM Studio.</param>
    /// <param name="embeddingModels">Модели-кандидаты для эмбеддингов.</param>
    /// <param name="cancellationToken">Токен отмены для запроса.</param>
    /// <returns>Загруженная embedding-модель или <see langword="null"/>, если ни одну не удалось определить.</returns>
    private async Task<LmStudioModelInfo?>
        FindLoadedEmbeddingModelAsync(
            string endpoint,
            List<LmStudioModelInfo> embeddingModels,
            CancellationToken cancellationToken)
    {
        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{endpoint}/models");

            AddAuthorization(request);

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            using var document =
                JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty(
                    "data",
                    out var dataElement))
            {
                return null;
            }

            var loadedKeys =
                new HashSet<string>(
                    dataElement
                        .EnumerateArray()
                        .Select(model =>
                            GetStringProperty(
                                model,
                                "id"))
                        .Where(id =>
                            !string.IsNullOrWhiteSpace(id))
                        .Select(id => id!),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var embeddingModel
                     in embeddingModels)
            {
                if (loadedKeys.Contains(
                        embeddingModel.Key))
                {
                    return embeddingModel;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Добавляет настроенный bearer-токен LM Studio к одному HTTP-запросу.
    /// </summary>
    /// <param name="request">Запрос, который нужно авторизовать.</param>
    private void AddAuthorization(
        HttpRequestMessage request)
    {
        var apiKey =
            Environment.GetEnvironmentVariable(
                "EMBEDD_KEY");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    apiKey);
        }
    }

    /// <summary>
    /// Преобразует совместимый с OpenAI endpoint в соответствующий собственный API endpoint LM Studio.
    /// </summary>
    /// <param name="endpoint">совместимый с OpenAI endpoint.</param>
    /// <returns>собственный <c>/api/v1</c> endpoint.</returns>
    private static string GetNativeApiEndpoint(
        string endpoint)
    {
        endpoint =
            endpoint.TrimEnd('/');

        if (endpoint.EndsWith(
                "/v1",
                StringComparison.OrdinalIgnoreCase))
        {
            return endpoint[..^3] + "/api/v1";
        }

        return endpoint + "/api/v1";
    }

    /// <summary>
    /// Читает строковое свойство JSON, если оно существует и содержит строковое значение.
    /// </summary>
    /// <param name="element">Проверяемый объект JSON.</param>
    /// <param name="propertyName">Имя читаемого свойства.</param>
    /// <returns>Значение свойства или <see langword="null"/>, если оно отсутствует либо не является строкой.</returns>
    private static string? GetStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return null;
        }

        return property.ValueKind ==
               JsonValueKind.String
            ? property.GetString()
            : null;
    }

    /// <summary>
    /// Записывает выбранную embedding-модель в stderr для диагностики.
    /// </summary>
    /// <param name="model">Метаданные выбранной модели.</param>
    private static void LogSelectedModel(
        LmStudioModelInfo model)
    {
        Console.Error.WriteLine(
            $"Embedding model selected: {model.Key}");

        Console.Error.WriteLine(
            $"Embedding model name: {model.DisplayName}");
    }

    /// <summary>
    /// Содержит идентификатор модели и её классификацию, возвращённые LM Studio.
    /// </summary>
    private sealed class LmStudioModelInfo
    {
        /// <summary>
        /// Получает LM Studio модель идентификатор.
        /// </summary>
        public string Key { get; init; } =
            string.Empty;

        /// <summary>
        /// Получает тип модели, возвращённый LM Studio (например, "embedding" или "llm").
        /// </summary>
        public string Type { get; init; } =
            string.Empty;

        /// <summary>
        /// Получает удобочитаемое имя модели.
        /// </summary>
        public string DisplayName { get; init; } =
            string.Empty;
    }
}
