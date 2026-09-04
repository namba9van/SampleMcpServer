using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Services;
/// <summary>
/// Определяет и проверяет endpoint LM Studio, совместимый с OpenAI.
/// </summary>
public sealed class LmStudioEndpoint
{
    private readonly HttpClient _httpClient;

    private readonly string? _configuredEndpoint;

    private readonly string? _apiKey;

    private string? _cachedEndpoint;
    /// <summary>
    /// Инициализирует обнаружение endpoint с использованием настроек endpoint и API-ключа из переменных окружения.
    /// </summary>
    public LmStudioEndpoint()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(800)
        };

        _configuredEndpoint =
            Environment.GetEnvironmentVariable(
                "EMBEDD_ENDPOINT");

        _apiKey =
            Environment.GetEnvironmentVariable(
                "EMBEDD_KEY");
    }
    /// <summary>
    /// Возвращает настроенный или автоматически обнаруженный endpoint LM Studio.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для обнаружения endpoint.</param>
    /// <returns>Нормализованный совместимый с OpenAI endpoint LM Studio.</returns>
    /// <exception cref="InvalidOperationException">Возникает, когда не удаётся найти доступный endpoint.</exception>
    public async Task<string> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(
                _configuredEndpoint))
        {
            return Normalize(
                _configuredEndpoint);
        }

        if (!string.IsNullOrWhiteSpace(
                _cachedEndpoint))
        {
            return _cachedEndpoint;
        }

        var endpoint =
            await FindAsync(
                cancellationToken);

        if (endpoint is null)
        {
            throw new InvalidOperationException(
                "Не удалось автоматически определить " +
                "адрес LM Studio API.");
        }

        _cachedEndpoint =
            endpoint;

        Console.Error.WriteLine(
            $"LM Studio API detected: {endpoint}");

        return endpoint;
    }
    /// <summary>
    /// Проверяет, отвечает ли указанный endpoint LM Studio на запрос списка моделей.
    /// </summary>
    /// <param name="endpoint">Проверяемый endpoint.</param>
    /// <param name="cancellationToken">Токен отмены для запроса.</param>
    /// <returns><see langword="true"/>, если endpoint отвечает успешно или требует аутентификации; иначе <see langword="false"/>.</returns>
    public async Task<bool> IsAvailableAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{Normalize(endpoint)}/models");

            AddAuthorization(request);

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            return response.IsSuccessStatusCode ||
                   response.StatusCode ==
                       HttpStatusCode.Unauthorized;
        }
        catch
        {
            return false;
        }
    }
    /// <summary>
    /// Проверяет локальные IPv4-адреса и доступные порты в поиске endpoint LM Studio.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для обнаружения.</param>
    /// <returns>Первый доступный endpoint или <see langword="null"/>, если ни один не найден.</returns>
    private async Task<string?> FindAsync(
        CancellationToken cancellationToken)
    {
        var addresses =
            GetLocalIPv4Addresses()
                .Distinct()
                .ToList();

        addresses.Insert(
            0,
            IPAddress.Loopback);

        var preferredPorts =
            new[]
            {
                1234
            };

        foreach (var port in preferredPorts)
        {
            var endpoint =
                await FindOnPortAsync(
                    addresses,
                    port,
                    cancellationToken);

            if (endpoint is not null)
            {
                return endpoint;
            }
        }

        for (var port = 1235; port <= 1300; port++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endpoint =
                await FindOnPortAsync(
                    addresses,
                    port,
                    cancellationToken);

            if (endpoint is not null)
            {
                return endpoint;
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет все адреса-кандидаты на одном TCP-порту.
    /// </summary>
    /// <param name="addresses">Локальные IPv4-адреса для проверки.</param>
    /// <param name="port">Проверяемый TCP-порт.</param>
    /// <param name="cancellationToken">Токен отмены для проверки.</param>
    /// <returns>Первый валидный endpoint LM Studio или <see langword="null"/>.</returns>
    private async Task<string?>
        FindOnPortAsync(
            IReadOnlyCollection<IPAddress> addresses,
            int port,
            CancellationToken cancellationToken)
    {
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endpoint =
                $"http://{address}:{port}/v1";

            Console.Error.WriteLine(
                $"Checking LM Studio API: {endpoint}");

            if (await IsLmStudioAsync(
                    endpoint,
                    cancellationToken))
            {
                return endpoint;
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет, принадлежит ли указанный endpoint серверу LM Studio.
    /// </summary>
    /// <param name="endpoint">Проверяемый endpoint.</param>
    /// <param name="cancellationToken">Токен отмены для запроса.</param>
    /// <returns><see langword="true"/> при успешном ответе или HTTP 401; иначе <see langword="false"/>.</returns>
    private async Task<bool>
        IsLmStudioAsync(
            string endpoint,
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

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode ==
                HttpStatusCode.Unauthorized)
            {
                Console.Error.WriteLine(
                    $"LM Studio detected at " +
                    $"{endpoint} " +
                    "(authentication required).");

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Добавляет настроенный bearer-токен к одному HTTP-запросу, если он доступен.
    /// </summary>
    /// <param name="request">Запрос, которому нужно добавить заголовок авторизации.</param>
    private void AddAuthorization(
        HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(
                _apiKey))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    _apiKey);
        }
    }

    /// <summary>
    /// Возвращает IPv4-адреса, относящиеся к текущим активным сетевым интерфейсам.
    /// </summary>
    /// <returns>Активные не loopback-адреса IPv4.</returns>
    private static IEnumerable<IPAddress>
        GetLocalIPv4Addresses()
    {
        return NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus ==
                OperationalStatus.Up)
            .SelectMany(networkInterface =>
                networkInterface
                    .GetIPProperties()
                    .UnicastAddresses)
            .Select(unicast =>
                unicast.Address)
            .Where(address =>
                address.AddressFamily ==
                AddressFamily.InterNetwork)
            .Where(address =>
                !IPAddress.IsLoopback(address));
    }

    /// <summary>
    /// Удаляет завершающие косые черты из URI endpoint.
    /// </summary>
    /// <param name="endpoint">Endpoint для нормализации.</param>
    /// <returns>Endpoint без завершающих косых черт.</returns>
    private static string Normalize(
        string endpoint)
    {
        return endpoint.TrimEnd('/');
    }
}
