using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace Services;

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

    public EmbeddingService(
        LmStudioEndpoint endpoint,
        LmStudioModelDiscovery modelDiscovery)
    {
        _endpoint = endpoint;
        _modelDiscovery = modelDiscovery;
    }

    public string? CurrentEndpoint =>
        _currentEndpoint;

    public string? CurrentModel =>
        _currentModel;

    public async Task<
        IEmbeddingGenerator<string, Embedding<float>>>
        GetGeneratorAsync(
            string? preferredEndpoint = null,
            string? preferredModel = null,
            CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine(
            "EmbeddingService: GetGeneratorAsync started.");

        // Если generator уже создан в рамках текущего
        // процесса, повторно ничего не инициализируем.
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
            // Пока мы ждали lock, другой запрос мог уже
            // выполнить инициализацию.
            if (_generator is not null)
            {
                Console.Error.WriteLine(
                    "EmbeddingService: generator was initialized " +
                    "by another request.");

                return _generator;
            }

            /*
             * 1. Получаем endpoint LM Studio.
             *
             * Если preferredEndpoint задан,
             * сначала пытаемся использовать его.
             */
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

            /*
             * 2. Получаем embedding model.
             *
             * Если preferredModel задан, используем его.
             * Иначе выполняем автоматический discovery.
             */
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

            /*
             * 3. Получаем API key LM Studio.
             */
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

            /*
             * 4. Создаём OpenAI-compatible client.
             */
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

            /*
             * 5. Создаём embedding generator.
             */
            Console.Error.WriteLine(
                $"EmbeddingService: creating embedding " +
                $"generator for model: {model}");

            _generator =
                client
                    .GetEmbeddingClient(
                        model)
                    .AsIEmbeddingGenerator();

            /*
             * 6. Сохраняем текущую конфигурацию.
             */
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