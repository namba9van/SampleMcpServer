using System.Net.Http.Headers;
using System.Text.Json;

namespace Services;

public sealed class LmStudioModelDiscovery
{
    private readonly LmStudioEndpoint _endpoint;
    private readonly HttpClient _httpClient;

    private readonly string? _configuredModel;

    private string? _cachedModel;

    public LmStudioModelDiscovery(
        LmStudioEndpoint endpoint)
    {
        _endpoint = endpoint;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        // Необязательный override.
        // Если задан, используем именно эту модель.
        _configuredModel =
            Environment.GetEnvironmentVariable("EMBEDD_MODEL");
    }

    public async Task<string> GetEmbeddingModelAsync(
        CancellationToken cancellationToken = default)
    {
        // 1. Явно заданная модель имеет приоритет.
        if (!string.IsNullOrWhiteSpace(_configuredModel))
        {
            return _configuredModel.Trim();
        }

        // 2. Если уже нашли модель, повторно не ищем.
        if (!string.IsNullOrWhiteSpace(_cachedModel))
        {
            return _cachedModel;
        }

        // 3. Находим endpoint LM Studio.
        var endpoint =
            await _endpoint.GetAsync(cancellationToken);

        // endpoint имеет вид:
        // http://10.8.1.1:1234/v1
        //
        // Native LM Studio API:
        // http://10.8.1.1:1234/api/v1
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

        // Если доступна ровно одна embedding-модель,
        // выбор однозначен.
        if (embeddingModels.Count == 1)
        {
            _cachedModel =
                embeddingModels[0].Key;

            LogSelectedModel(
                embeddingModels[0]);

            return _cachedModel;
        }

        // Если моделей несколько, пытаемся выбрать
        // загруженную модель через OpenAI-compatible API.
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

        // Невозможно безопасно выбрать одну модель.
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

    private static void LogSelectedModel(
        LmStudioModelInfo model)
    {
        Console.Error.WriteLine(
            $"Embedding model selected: {model.Key}");

        Console.Error.WriteLine(
            $"Embedding model name: {model.DisplayName}");
    }

    private sealed class LmStudioModelInfo
    {
        public string Key { get; init; } =
            string.Empty;

        public string Type { get; init; } =
            string.Empty;

        public string DisplayName { get; init; } =
            string.Empty;
    }
}