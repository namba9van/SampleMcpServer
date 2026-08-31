using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace Services;
/// <summary>
/// Лениво создаёт и кэширует генератор embedding, используемый подсистемой RAG.
/// </summary>
public sealed class EmbeddingService
{
    private readonly LmStudioEndpoint _endpoint;

    private readonly LmStudioModelDiscovery _modelDiscovery;

    private readonly SemaphoreSlim _lock =
        new(1, 1);

    private IEmbeddingGenerator<string, Embedding<float>>?
        _generator;

    private string? _currentEndpoint;

    private string? _currentModel;
    /// <summary>
    /// Инициализирует сервис embedding с endpoint LM Studio и зависимостями для обнаружения модели.
    /// </summary>
    /// <param name="endpoint">сервис that определяет LM Studio endpoint.</param>
    /// <param name="modelDiscovery">сервис that определяет embedding модель.</param>
    public EmbeddingService(
        LmStudioEndpoint endpoint,
        LmStudioModelDiscovery modelDiscovery)
    {
        _endpoint = endpoint;
        _modelDiscovery = modelDiscovery;
    }
    /// <summary>
    /// Получает endpoint, используемый кэшированным генератором embedding, если генератор уже инициализирован.
    /// </summary>
    public string? CurrentEndpoint =>
        _currentEndpoint;
    /// <summary>
    /// Получает модель используемый по кэшированный embedding генератор, когда инициализирован.
    /// </summary>
    public string? CurrentModel =>
        _currentModel;
    /// <summary>
    /// Создаёт и caches один embedding генератор для запрошенный LM Studio конфигурации.
    /// </summary>
    /// <param name="preferredEndpoint">необязательный endpoint переопределение для try первый.</param>
    /// <param name="preferredModel">необязательный модель переопределение.</param>
    /// <param name="cancellationToken">отмена токен для инициализации.</param>
    /// <returns>кэшированный или заново созданный embedding генератор.</returns>
    /// <exception cref="InvalidOperationException">возникает когда LM Studio или embedding конфигурации cannot быть инициализирован.</exception>
    public async Task<
        IEmbeddingGenerator<string, Embedding<float>>>
        GetGeneratorAsync(
            string? preferredEndpoint = null,
            string? preferredModel = null,
            CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine(
            "EmbeddingService: GetGeneratorAsync started.");

        if (_generator is not null)
        {
            Console.Error.WriteLine(
                "EmbeddingService: using cached generator.");

            return _generator;
        }

        Console.Error.WriteLine(
            "EmbeddingService: waiting for initialization lock.");

        await _lock.WaitAsync(
            cancellationToken);

        Console.Error.WriteLine(
            "EmbeddingService: initialization lock acquired.");

        try
        {
            if (_generator is not null)
            {
                Console.Error.WriteLine(
                    "EmbeddingService: generator was initialized " +
                    "by another request.");

                return _generator;
            }

            string endpoint;

            if (!string.IsNullOrWhiteSpace(
                    preferredEndpoint))
            {
                Console.Error.WriteLine(
                    $"EmbeddingService: checking preferred " +
                    $"endpoint: {preferredEndpoint}");

                var preferredAvailable =
                    await _endpoint.IsAvailableAsync(
                        preferredEndpoint,
                        cancellationToken);

                if (preferredAvailable)
                {
                    endpoint =
                        preferredEndpoint.TrimEnd('/');

                    Console.Error.WriteLine(
                        "EmbeddingService: using preferred " +
                        "LM Studio endpoint.");
                }
                else
                {
                    Console.Error.WriteLine(
                        "EmbeddingService: preferred endpoint " +
                        "is unavailable.");

                    endpoint =
                        await _endpoint.GetAsync(
                            cancellationToken);
                }
            }
            else
            {
                Console.Error.WriteLine(
                    "EmbeddingService: resolving LM Studio endpoint.");

                endpoint =
                    await _endpoint.GetAsync(
                        cancellationToken);
            }

            Console.Error.WriteLine(
                $"EmbeddingService: endpoint resolved: " +
                $"{endpoint}");

            string model;

            if (!string.IsNullOrWhiteSpace(
                    preferredModel))
            {
                model =
                    preferredModel.Trim();

                Console.Error.WriteLine(
                    $"EmbeddingService: using preferred " +
                    $"embedding model: {model}");
            }
            else
            {
                Console.Error.WriteLine(
                    "EmbeddingService: resolving embedding model.");

                model =
                    await _modelDiscovery
                        .GetEmbeddingModelAsync(
                            cancellationToken);

                Console.Error.WriteLine(
                    $"EmbeddingService: model resolved: {model}");
            }

            var key =
                Environment.GetEnvironmentVariable(
                    "EMBEDD_KEY");

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "EMBEDD_KEY не задан. " +
                    "LM Studio требует API token.");
            }

            Console.Error.WriteLine(
                "EmbeddingService: API key is configured.");

            Console.Error.WriteLine(
                "EmbeddingService: creating OpenAI client.");

            var options =
                new OpenAIClientOptions
                {
                    Endpoint =
                        new Uri(endpoint)
                };

            var credential =
                new ApiKeyCredential(key);

            var client =
                new OpenAIClient(
                    credential,
                    options);

            Console.Error.WriteLine(
                $"EmbeddingService: creating embedding " +
                $"generator for model: {model}");

            _generator =
                client
                    .GetEmbeddingClient(
                        model)
                    .AsIEmbeddingGenerator();

            _currentEndpoint =
                endpoint;

            _currentModel =
                model;

            Console.Error.WriteLine(
                $"EmbeddingService: initialized successfully. " +
                $"Endpoint={endpoint}, Model={model}");

            return _generator;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(
                "EmbeddingService: initialization canceled.");

            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"EmbeddingService: initialization failed: " +
                $"{ex}");

            throw;
        }
        finally
        {
            _lock.Release();

            Console.Error.WriteLine(
                "EmbeddingService: initialization lock released.");
        }
    }
}
