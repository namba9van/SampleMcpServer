using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace Services;
/// <summary>
/// Lazily creates and caches the embedding generator used by the RAG subsystem.
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
    /// Initializes the embedding service with LM Studio endpoint and model discovery dependencies.
    /// </summary>
    /// <param name="endpoint">The service that resolves the LM Studio endpoint.</param>
    /// <param name="modelDiscovery">The service that resolves the embedding model.</param>
    public EmbeddingService(
        LmStudioEndpoint endpoint,
        LmStudioModelDiscovery modelDiscovery)
    {
        _endpoint = endpoint;
        _modelDiscovery = modelDiscovery;
    }
    /// <summary>
    /// Gets the endpoint used by the cached embedding generator, when initialized.
    /// </summary>
    public string? CurrentEndpoint =>
        _currentEndpoint;
    /// <summary>
    /// Gets the model used by the cached embedding generator, when initialized.
    /// </summary>
    public string? CurrentModel =>
        _currentModel;
    /// <summary>
    /// Creates and caches an embedding generator for the requested LM Studio configuration.
    /// </summary>
    /// <param name="preferredEndpoint">An optional endpoint override to try first.</param>
    /// <param name="preferredModel">An optional model override.</param>
    /// <param name="cancellationToken">The cancellation token for initialization.</param>
    /// <returns>The cached or newly created embedding generator.</returns>
    /// <exception cref="InvalidOperationException">Thrown when LM Studio or the embedding configuration cannot be initialized.</exception>
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
