using System.Net.Http.Headers;
using System.Text.Json;

namespace Services;
/// <summary>
/// Определяет доступную в LM Studio embedding-модель.
/// </summary>
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
    /// Инициализирует модель обнаружение и читает необязательный модель переопределение.
    /// </summary>
    /// <param name="endpoint">LM Studio endpoint resolver.</param>
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
    /// <param name="cancellationToken">отмена токен для модель обнаружение запросы.</param>
    /// <returns>выбранный embedding модель идентификатор.</returns>
    /// <exception cref="InvalidOperationException">возникает когда не embedding модель может быть выбранный unambiguously.</exception>
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
    /// Читает собственный LM Studio модель каталог.
    /// </summary>
    /// <param name="nativeEndpoint">собственный LM Studio API endpoint.</param>
    /// <param name="cancellationToken">отмена токен для запрос.</param>
    /// <returns>модели возвращённый по LM Studio.</returns>
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
    /// <param name="endpoint">совместимый с OpenAI LM Studio endpoint.</param>
    /// <param name="embeddingModels">кандидат embedding модели.</param>
    /// <param name="cancellationToken">отмена токен для запрос.</param>
    /// <returns>загруженный embedding-модель, или <see langword="null"/> когда ни один может быть определён.</returns>
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
    /// Добавляет настроенный LM Studio bearer токен для один HTTP запрос.
    /// </summary>
    /// <param name="request">запрос для авторизовать.</param>
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
    /// <param name="element">объект JSON для проверить.</param>
    /// <param name="propertyName">свойство имя для чтения.</param>
    /// <returns>свойство значение, или <see langword="null"/> когда it является отсутствующий или нестроковый.</returns>
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
    /// Записывает выбранный embedding модель для stderr для диагностика.
    /// </summary>
    /// <param name="model">выбранный модель метаданные.</param>
    private static void LogSelectedModel(
        LmStudioModelInfo model)
    {
        Console.Error.WriteLine(
            $"Embedding model selected: {model.Key}");

        Console.Error.WriteLine(
            $"Embedding model name: {model.DisplayName}");
    }

    /// <summary>
    /// Содержит модель идентификатор и классификация возвращённый по LM Studio.
    /// </summary>
    private sealed class LmStudioModelInfo
    {
        /// <summary>
        /// Получает LM Studio модель идентификатор.
        /// </summary>
        public string Key { get; init; } =
            string.Empty;

        /// <summary>
        /// Описывает назначение элемента.
        /// </summary>
        public string Type { get; init; } =
            string.Empty;

        /// <summary>
        /// Получает удобочитаемый модель имя.
        /// </summary>
        public string DisplayName { get; init; } =
            string.Empty;
    }
}
