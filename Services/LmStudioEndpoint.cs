using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Services;

public sealed class LmStudioEndpoint
{
    private readonly HttpClient _httpClient;

    private readonly string? _configuredEndpoint;

    private readonly string? _apiKey;

    private string? _cachedEndpoint;

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

    public async Task<string> GetAsync(
        CancellationToken cancellationToken = default)
    {
        // Ручной override имеет высший приоритет.
        if (!string.IsNullOrWhiteSpace(
                _configuredEndpoint))
        {
            return Normalize(
                _configuredEndpoint);
        }

        // Endpoint уже найден в текущем процессе.
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
    /// Быстрая проверка endpoint, который был
    /// сохранён ранее.
    /// </summary>
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

    private async Task<string?> FindAsync(
        CancellationToken cancellationToken)
    {
        var addresses =
            GetLocalIPv4Addresses()
                .Distinct()
                .ToList();

        // Loopback проверяем всегда.
        addresses.Insert(
            0,
            IPAddress.Loopback);

        /*
         * Сначала проверяем стандартный порт LM Studio.
         * Согласно документации LM Studio, по умолчанию
         * сервер доступен на localhost:1234.
         */
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

        /*
         * Если стандартный порт не найден,
         * проверяем небольшой диапазон.
         *
         * Это позволяет работать после изменения
         * пользователем порта LM Studio.
         */
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

            /*
             * 401 означает, что HTTP-сервер существует
             * и endpoint правильный, но включена
             * authentication.
             */
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

    private static string Normalize(
        string endpoint)
    {
        return endpoint.TrimEnd('/');
    }
}