using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Services;
/// <summary>
/// Resolves and validates the OpenAI-compatible LM Studio endpoint.
/// </summary>
public sealed class LmStudioEndpoint
{
    private readonly HttpClient _httpClient;

    private readonly string? _configuredEndpoint;

    private readonly string? _apiKey;

    private string? _cachedEndpoint;
    /// <summary>
    /// Initializes endpoint discovery using environment-based endpoint and API-key settings.
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
    /// Returns a configured or automatically discovered LM Studio endpoint.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for endpoint discovery.</param>
    /// <returns>The normalized OpenAI-compatible LM Studio endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no reachable endpoint can be found.</exception>
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
    /// Checks whether the specified LM Studio endpoint responds to a model request.
    /// </summary>
    /// <param name="endpoint">The endpoint to check.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns><see langword="true"/> when the endpoint responds successfully or requires authentication; otherwise, <see langword="false"/>.</returns>
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
    /// Searches local IPv4 addresses and candidate ports for a reachable LM Studio endpoint.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for discovery.</param>
    /// <returns>The first reachable endpoint, or <see langword="null"/> when none is found.</returns>
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
    /// Checks all candidate local addresses on one TCP port.
    /// </summary>
    /// <param name="addresses">The local IPv4 addresses to probe.</param>
    /// <param name="port">The TCP port to probe.</param>
    /// <param name="cancellationToken">The cancellation token for the probes.</param>
    /// <returns>The first valid LM Studio endpoint, or <see langword="null"/>.</returns>
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
    /// Determines whether an HTTP endpoint is a reachable LM Studio API endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint to validate.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns><see langword="true"/> for a successful response or HTTP 401; otherwise, <see langword="false"/>.</returns>
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
    /// Adds the configured bearer token to an HTTP request when available.
    /// </summary>
    /// <param name="request">The request that should receive the authorization header.</param>
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
    /// Returns IPv4 addresses belonging to currently active network interfaces.
    /// </summary>
    /// <returns>The active non-loopback IPv4 addresses.</returns>
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
    /// Removes trailing slashes from an endpoint URI.
    /// </summary>
    /// <param name="endpoint">The endpoint to normalize.</param>
    /// <returns>The endpoint without trailing slashes.</returns>
    private static string Normalize(
        string endpoint)
    {
        return endpoint.TrimEnd('/');
    }
}
