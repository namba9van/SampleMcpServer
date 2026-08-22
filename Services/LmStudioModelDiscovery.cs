using System.Net.Http.Headers;
using System.Text.Json;

namespace Services;
/// <summary>
/// Discovers the embedding model available in LM Studio.
/// </summary>
/// <summary>
/// Discovers the embedding model available in LM Studio.
/// </summary>
public sealed class LmStudioModelDiscovery
{
    private readonly LmStudioEndpoint _endpoint;
    private readonly HttpClient _httpClient;

    private readonly string? _configuredModel;

    private string? _cachedModel;
    /// <summary>
    /// Initializes model discovery and reads the optional model override.
    /// </summary>
    /// <param name="endpoint">The LM Studio endpoint resolver.</param>
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
    /// Selects an embedding model using an explicit override, automatic discovery, or the loaded-model list.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for model discovery requests.</param>
    /// <returns>The selected embedding model identifier.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no embedding model can be selected unambiguously.</exception>
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
    /// Reads the native LM Studio model catalog.
    /// </summary>
    /// <param name="nativeEndpoint">The native LM Studio API endpoint.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The models returned by LM Studio.</returns>
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
    /// Finds an embedding model reported as loaded by the OpenAI-compatible models endpoint.
    /// </summary>
    /// <param name="endpoint">The OpenAI-compatible LM Studio endpoint.</param>
    /// <param name="embeddingModels">The candidate embedding models.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The loaded embedding model, or <see langword="null"/> when none can be identified.</returns>
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
    /// Adds the configured LM Studio bearer token to an HTTP request.
    /// </summary>
    /// <param name="request">The request to authorize.</param>
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
    /// Converts an OpenAI-compatible endpoint into the corresponding native LM Studio API endpoint.
    /// </summary>
    /// <param name="endpoint">The OpenAI-compatible endpoint.</param>
    /// <returns>The native <c>/api/v1</c> endpoint.</returns>
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
    /// Reads a JSON string property when it exists and has a string value.
    /// </summary>
    /// <param name="element">The JSON object to inspect.</param>
    /// <param name="propertyName">The property name to read.</param>
    /// <returns>The property value, or <see langword="null"/> when it is absent or non-string.</returns>
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
    /// Writes the selected embedding model to stderr for diagnostics.
    /// </summary>
    /// <param name="model">The selected model metadata.</param>
    private static void LogSelectedModel(
        LmStudioModelInfo model)
    {
        Console.Error.WriteLine(
            $"Embedding model selected: {model.Key}");

        Console.Error.WriteLine(
            $"Embedding model name: {model.DisplayName}");
    }

    /// <summary>
    /// Contains the model identifier and classification returned by LM Studio.
    /// </summary>
    private sealed class LmStudioModelInfo
    {
        /// <summary>
        /// Gets the LM Studio model identifier.
        /// </summary>
        public string Key { get; init; } =
            string.Empty;

        /// <summary>
        /// Gets the model type reported by LM Studio.
        /// </summary>
        public string Type { get; init; } =
            string.Empty;

        /// <summary>
        /// Gets the human-readable model name.
        /// </summary>
        public string DisplayName { get; init; } =
            string.Empty;
    }
}
