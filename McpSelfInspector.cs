using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// Встроенный диагностический клиент MCP. Запускает отдельный экземпляр сервера
/// через stdio и проверяет базовый жизненный цикл протокола и доступность инструментов.
/// </summary>
internal static class McpSelfInspector
{
    private const string ProtocolVersion = "2024-11-05";

    public static async Task<int> RunSelfTestAsync(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" SampleMcpServer — самопроверка MCP");
        Console.WriteLine("========================================");

        await using var session = await StartServerAsync();
        var passed = 0;
        var failed = 0;

        async Task Check(string name, Func<Task<bool>> action)
        {
            try
            {
                if (await action())
                {
                    passed++;
                    Console.WriteLine($"[PASS] {name}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"[FAIL] {name}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        JsonDocument? initialize = null;
        JsonDocument? tools = null;
        List<string>? toolNames = null;

        await Check("Запуск процесса", () => Task.FromResult(session.Process.HasExited == false));

        await Check("MCP initialize", async () =>
        {
            initialize = await session.RequestAsync("initialize", new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new { name = "SampleMcpServer-SelfInspector", version = "1.0.0" }
            });
            return HasResult(initialize);
        });

        await Check("Уведомление initialized", async () =>
        {
            await session.NotifyAsync("notifications/initialized", new { });
            return true;
        });

        await Check("tools/list", async () =>
        {
            tools = await session.RequestAsync("tools/list", new { });
            return HasResult(tools) && tools.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("tools", out var list)
                && list.ValueKind == JsonValueKind.Array;
        });

        if (tools is not null && TryGetToolNames(tools, out var discoveredToolNames))
        {
            toolNames = discoveredToolNames;
            Console.WriteLine();
            Console.WriteLine("Доступные MCP-инструменты:");
            foreach (var name in toolNames)
                Console.WriteLine($"  • {name}");

            var addTool = toolNames.FirstOrDefault(x =>
                string.Equals(x, "add", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, "Add", StringComparison.OrdinalIgnoreCase));

            if (addTool is not null)
            {
                await Check("Вызов калькулятора", async () =>
                {
                    using var response = await session.RequestAsync("tools/call", new
                    {
                        name = addTool,
                        arguments = new { a = 2, b = 3 }
                    });
                    return HasResult(response) && !HasError(response);
                });
            }
        }

        await Check("Корректный JSON-RPC stdout", () => Task.FromResult(session.ProtocolError is null));

        Console.WriteLine();
        Console.WriteLine("----------------------------------------");
        Console.WriteLine($"Результат: {passed} успешно, {failed} ошибок");
        Console.WriteLine(failed == 0
            ? "MCP-СЕРВЕР: ИСПРАВЕН"
            : "MCP-СЕРВЕР: ТРЕБУЕТ ПРОВЕРКИ");
        Console.WriteLine("----------------------------------------");

        return failed == 0 ? 0 : 1;
    }

    public static async Task<int> RunInteractiveAsync(string[] args)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine(" SampleMcpServer — локальный MCP-инспектор");
            Console.WriteLine("========================================");
            Console.WriteLine("1. Полная проверка");
            Console.WriteLine("2. Показать MCP-инструменты");
            Console.WriteLine("3. Проверить JSON-RPC initialize");
            Console.WriteLine("0. Выход");
            Console.Write("Выберите действие: ");

            var choice = Console.ReadLine()?.Trim();
            if (choice == "0")
                return 0;

            if (choice == "1")
            {
                Console.WriteLine();
                return await RunSelfTestAsync(args);
            }

            if (choice is "2" or "3")
            {
                await using var session = await StartServerAsync();
                if (choice == "3")
                {
                    using var response = await session.RequestAsync("initialize", new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "SampleMcpServer-SelfInspector", version = "1.0.0" }
                    });
                    PrintJson(response);
                }
                else
                {
                    using var init = await session.RequestAsync("initialize", new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "SampleMcpServer-SelfInspector", version = "1.0.0" }
                    });
                    await session.NotifyAsync("notifications/initialized", new { });
                    using var response = await session.RequestAsync("tools/list", new { });
                    PrintJson(response);
                }

                Console.WriteLine();
                Console.WriteLine("Нажмите Enter для возврата в меню...");
                Console.ReadLine();
            }
        }
    }

    private static async Task<McpProcessSession> StartServerAsync()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к текущему процессу.");

        // При запуске через `dotnet run` текущим процессом является dotnet,
        // поэтому дочерний сервер запускается из собранной DLL.
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, typeof(McpSelfInspector).Assembly.GetName().Name + ".dll");
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Не удалось определить путь к сборке MCP-сервера.");

            return await McpProcessSession.StartAsync(processPath, $"\"{assemblyPath}\"");
        }

        return await McpProcessSession.StartAsync(processPath, "");
    }

    private static bool HasResult(JsonDocument document) =>
        document.RootElement.TryGetProperty("result", out _);

    private static bool HasError(JsonDocument document) =>
        document.RootElement.TryGetProperty("error", out _);

    private static bool TryGetToolNames(JsonDocument document, out List<string> names)
    {
        names = [];
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("tools", out var tools) ||
            tools.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                names.Add(name.GetString()!);
        }

        return true;
    }

    private static void PrintJson(JsonDocument document)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) }));
    }

    private sealed class McpProcessSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private int _nextId;

        public Exception? ProtocolError { get; private set; }
        public Process Process => _process;

        private McpProcessSession(Process process)
        {
            _process = process;
            _writer = process.StandardInput;
            _reader = process.StandardOutput;
        }

        public static async Task<McpProcessSession> StartAsync(string executable, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            // Диагностический процесс получает те же переменные окружения, что и родитель.
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Не удалось запустить MCP-сервер.");

            _ = Task.Run(async () =>
            {
                while (!process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line is null)
                        break;
                    Console.Error.WriteLine($"[server] {line}");
                }
            });

            return await Task.FromResult(new McpProcessSession(process));
        }

        public async Task<JsonDocument> RequestAsync(string method, object parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var line = await _reader.ReadLineAsync(timeout.Token);
                if (line is null)
                    throw new EndOfStreamException("MCP-сервер завершил stdout до получения ответа.");
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    if (root.TryGetProperty("id", out var responseId) &&
                        responseId.ValueKind == JsonValueKind.Number &&
                        responseId.GetInt32() == id)
                        return document;

                    document.Dispose();
                }
                catch (JsonException ex)
                {
                    ProtocolError = ex;
                    throw new InvalidDataException($"MCP-сервер вернул невалидный JSON: {line}", ex);
                }
            }
        }

        public async Task<JsonDocument> RequestWithProgressAsync(
            string method,
            object parameters,
            Action<JsonElement>? onProgress)
        {
            var id = Interlocked.Increment(ref _nextId);
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters });

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            while (true)
            {
                var line = await _reader.ReadLineAsync(timeout.Token);
                if (line is null)
                    throw new EndOfStreamException("MCP-сервер завершил stdout до получения ответа.");
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    if (root.TryGetProperty("method", out var notificationMethod) &&
                        notificationMethod.ValueKind == JsonValueKind.String &&
                        string.Equals(
                            notificationMethod.GetString(),
                            "notifications/progress",
                            StringComparison.Ordinal))
                    {
                        onProgress?.Invoke(root);
                        document.Dispose();
                        continue;
                    }

                    if (root.TryGetProperty("id", out var responseId) &&
                        responseId.ValueKind == JsonValueKind.Number &&
                        responseId.GetInt32() == id)
                    {
                        return document;
                    }

                    document.Dispose();
                }
                catch (JsonException ex)
                {
                    ProtocolError = ex;
                    throw new InvalidDataException($"MCP-сервер вернул невалидный JSON: {line}", ex);
                }
            }
        }

        public async Task NotifyAsync(string method, object parameters)
        {
            await SendAsync(new { jsonrpc = "2.0", method, @params = parameters });
        }

        private async Task SendAsync(object message)
        {
            var json = JsonSerializer.Serialize(message);
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            catch
            {
                // Процесс диагностики уже мог завершиться самостоятельно.
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
