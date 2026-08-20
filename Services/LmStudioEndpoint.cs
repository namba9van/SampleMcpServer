using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text.Json;

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
                "Не удалось автоматически определить адрес " +
                "LM Studio API.");
        }

        _cachedEndpoint =
            endpoint;

        Console.Error.WriteLine(
            $"LM Studio API detected: {endpoint}");

        return endpoint;
    }

    /// <summary>
    /// Быстрая проверка уже известного endpoint.
    /// Не запускает lms и не перебирает IP-адреса.
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

            AddAuthorization(
                request);

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            Console.Error.WriteLine(
                $"Cached LM Studio endpoint " +
                $"{endpoint} returned " +
                $"{(int)response.StatusCode}.");

            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Cached LM Studio endpoint " +
                $"{endpoint} unavailable: " +
                $"{ex.Message}");

            return false;
        }
    }

    private async Task<string?> FindAsync(
        CancellationToken cancellationToken)
    {
        var port =
            await GetLmStudioPortAsync(
                cancellationToken);

        if (port is null)
        {
            return null;
        }

        var candidates =
            new List<string>();

        foreach (var address
                 in GetLocalIPv4Addresses())
        {
            candidates.Add(
                $"http://{address}:{port}/v1");
        }

        candidates.Add(
            $"http://127.0.0.1:{port}/v1");

        candidates.Add(
            $"http://localhost:{port}/v1");

        foreach (var candidate
                 in candidates.Distinct())
        {
            Console.Error.WriteLine(
                $"Checking LM Studio API: " +
                $"{candidate}");

            if (await IsLmStudioAsync(
                    candidate,
                    cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<int?>
        GetLmStudioPortAsync(
            CancellationToken cancellationToken)
    {
        try
        {
            var startInfo =
                new ProcessStartInfo
                {
                    FileName = "lms",
                    Arguments =
                        "server status --json --quiet",

                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

                    UseShellExecute = false,
                    CreateNoWindow = true
                };

            using var process =
                Process.Start(
                    startInfo);

            if (process is null)
            {
                return null;
            }

            var output =
                await process
                    .StandardOutput
                    .ReadToEndAsync(
                        cancellationToken);

            await process
                .WaitForExitAsync(
                    cancellationToken);

            if (process.ExitCode != 0)
            {
                return null;
            }

            using var json =
                JsonDocument.Parse(
                    output);

            var root =
                json.RootElement;

            if (!root.TryGetProperty(
                    "running",
                    out var running) ||
                !running.GetBoolean())
            {
                return null;
            }

            if (!root.TryGetProperty(
                    "port",
                    out var port))
            {
                return null;
            }

            var portNumber =
                port.GetInt32();

            Console.Error.WriteLine(
                $"LM Studio server port: " +
                $"{portNumber}");

            return portNumber;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to get LM Studio port: " +
                $"{ex.Message}");

            return null;
        }
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

            AddAuthorization(
                request);

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode ==
                System.Net.HttpStatusCode.Unauthorized)
            {
                Console.Error.WriteLine(
                    $"LM Studio detected at " +
                    $"{endpoint} " +
                    "(authentication required).");

                return true;
            }

            Console.Error.WriteLine(
                $"LM Studio probe {endpoint}: " +
                $"{(int)response.StatusCode} " +
                $"{response.StatusCode}");

            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"LM Studio probe {endpoint}: " +
                $"{ex.Message}");

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