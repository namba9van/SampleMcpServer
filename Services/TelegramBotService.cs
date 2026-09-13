using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Optional Telegram polling bridge. It turns messages from explicitly allowed chats
/// into durable agent jobs and sends the final result back through the notification pipeline.
/// </summary>
public sealed class TelegramBotService : BackgroundService
{
    private readonly TriggerAutomationService _automation;
    private readonly ModelMemoryService _memory;
    private readonly RuntimeGovernanceService _runtime;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private long? _nextOffset;

    public TelegramBotService(
        TriggerAutomationService automation,
        ModelMemoryService memory,
        RuntimeGovernanceService runtime,
        ILogger<TelegramBotService> logger)
    {
        _automation = automation;
        _memory = memory;
        _runtime = runtime;
        _logger = logger;
    }

    public TelegramBotStatus GetStatus()
    {
        var enabled = ReadBool("TELEGRAM_BOT_ENABLED", false);
        var tokenConfigured = !string.IsNullOrWhiteSpace(ReadToken());
        var allowedChats = ReadAllowedChatIds();
        return new TelegramBotStatus(enabled, tokenConfigured, allowedChats.Count, _nextOffset);
    }

    public async Task<TelegramSendResult> SendMessageAsync(
        string chatId,
        string text,
        CancellationToken ct = default)
    {
        var token = RequireToken();
        chatId = NormalizeChatId(chatId);
        if (!IsAllowedChat(chatId))
            throw new UnauthorizedAccessException($"Telegram chat '{chatId}' is not allow-listed.");

        var sent = await SendTelegramMessageAsync(token, chatId, text, ct);
        return new TelegramSendResult(chatId, sent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadBool("TELEGRAM_BOT_ENABLED", false))
        {
            _logger.LogInformation("Telegram bot bridge disabled (TELEGRAM_BOT_ENABLED=false).");
            return;
        }

        var token = ReadToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Telegram bot bridge enabled but TELEGRAM_BOT_TOKEN is empty.");
            return;
        }

        if (ReadAllowedChatIds().Count == 0)
        {
            _logger.LogWarning("Telegram bot bridge enabled but TELEGRAM_ALLOWED_CHAT_IDS is empty.");
            return;
        }

        if (ReadBool("TELEGRAM_DROP_PENDING_UPDATES", true))
            await DropPendingUpdatesAsync(token, stoppingToken);

        _logger.LogInformation("Telegram bot bridge started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(token, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram bot polling failed.");
                await Task.Delay(ReadInt("TELEGRAM_POLL_INTERVAL_MS", 1000, 250, 60_000), stoppingToken);
            }
        }
    }

    private async Task DropPendingUpdatesAsync(string token, CancellationToken ct)
    {
        var updates = await GetUpdatesAsync(token, null, timeoutSeconds: 0, limit: 100, ct);
        if (updates.Count == 0) return;
        _nextOffset = updates.Max(u => u.UpdateId) + 1;
        _logger.LogInformation("Telegram bot skipped {Count} pending updates.", updates.Count);
    }

    private async Task PollOnceAsync(string token, CancellationToken ct)
    {
        var timeout = ReadInt("TELEGRAM_POLL_TIMEOUT_SECONDS", 25, 1, 50);
        var updates = await GetUpdatesAsync(token, _nextOffset, timeout, limit: 20, ct);
        foreach (var update in updates)
        {
            _nextOffset = update.UpdateId + 1;
            await HandleUpdateAsync(token, update, ct);
        }
    }

    private async Task HandleUpdateAsync(string token, TelegramUpdate update, CancellationToken ct)
    {
        var message = update.Message ?? update.ChannelPost;
        if (message is null || string.IsNullOrWhiteSpace(message.Text)) return;

        var chatId = NormalizeChatId(message.ChatId.ToString());
        if (!IsAllowedChat(chatId))
        {
            await _runtime.AuditAsync(
                "telegram",
                "message",
                "denied",
                new { update.UpdateId, chatId, reason = "chat_not_allowed" },
                ct);
            return;
        }

        var text = message.Text.Trim();
        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            await SendTelegramMessageAsync(
                token,
                chatId,
                "Send a task message and I will queue it for the local Agent Host. Use /status to check that the bridge is alive.",
                ct);
            return;
        }

        if (text.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            var status = GetStatus();
            await SendTelegramMessageAsync(
                token,
                chatId,
                $"Telegram bridge is running. Allowed chats: {status.AllowedChatCount}. Next update offset: {status.NextOffset?.ToString() ?? "not set"}.",
                ct);
            return;
        }

        var budget = ReadInt("TELEGRAM_CONTEXT_TOKEN_BUDGET", 1800, 300, 12_000);
        var context = await _memory.BuildContextAsync(
            text,
            namespaceHints: null,
            candidateLimit: 18,
            tokenBudget: budget,
            maxItems: 7,
            minSimilarity: 0.15,
            includeMetadata: false,
            includeSuperseded: false,
            ct: ct);

        var contextJson = JsonSerializer.Serialize(new
        {
            context.Query,
            context.NamespaceHints,
            context.TokenBudget,
            context.EstimatedTokens,
            context.Returned,
            context.Context,
            context.Sources,
            context.HasOpenConflicts,
            context.Guidance,
            Telegram = new
            {
                ChatId = chatId,
                message.MessageId,
                message.FromUsername,
                update.UpdateId
            }
        });

        var prompt = $"""
Remote Telegram request from chat {chatId}, message {message.MessageId}.
Treat the Telegram message as the user's current request. Reply with a concise answer suitable for Telegram.

Telegram message:
{text}
""";

        var run = await _automation.EnqueueAgentJobAsync(
            $"telegram:{chatId}:{message.MessageId}:{update.UpdateId}",
            prompt,
            contextJson,
            ct);

        await _runtime.AuditAsync(
            "telegram",
            "queue",
            "ok",
            new { update.UpdateId, chatId, message.MessageId, run.RunId },
            ct);

        if (ReadBool("TELEGRAM_ACK_QUEUED", true))
            await SendTelegramMessageAsync(token, chatId, $"Queued as agent job {run.RunId}.", ct);
    }

    private async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        string token,
        long? offset,
        int timeoutSeconds,
        int limit,
        CancellationToken ct)
    {
        var payload = new
        {
            offset,
            timeout = timeoutSeconds,
            limit,
            allowed_updates = new[] { "message", "channel_post" }
        };

        using var request = CreateTelegramRequest(token, "getUpdates", payload);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException("Telegram getUpdates returned ok=false.");

        var result = new List<TelegramUpdate>();
        foreach (var item in root.GetProperty("result").EnumerateArray())
        {
            var updateId = item.GetProperty("update_id").GetInt64();
            result.Add(new TelegramUpdate(
                updateId,
                TryReadMessage(item, "message"),
                TryReadMessage(item, "channel_post")));
        }

        return result;
    }

    private async Task<bool> SendTelegramMessageAsync(string token, string chatId, string text, CancellationToken ct)
    {
        var chunks = SplitTelegramText(text);
        foreach (var chunk in chunks)
        {
            var payload = new
            {
                chat_id = chatId,
                text = chunk,
                disable_web_page_preview = true
            };

            using var request = CreateTelegramRequest(token, "sendMessage", payload);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }

        return chunks.Count > 0;
    }

    private static HttpRequestMessage CreateTelegramRequest(string token, string method, object payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"https://api.telegram.org/bot{token}/{method}"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static TelegramMessage? TryReadMessage(JsonElement update, string propertyName)
    {
        if (!update.TryGetProperty(propertyName, out var message)) return null;
        if (!message.TryGetProperty("chat", out var chat) ||
            !chat.TryGetProperty("id", out var chatIdElement) ||
            !chatIdElement.TryGetInt64(out var chatId))
        {
            return null;
        }

        if (!message.TryGetProperty("message_id", out var messageIdElement) ||
            !messageIdElement.TryGetInt32(out var messageId))
        {
            messageId = 0;
        }

        var text = message.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString()
            : null;

        string? username = null;
        if (message.TryGetProperty("from", out var from) &&
            from.TryGetProperty("username", out var usernameElement) &&
            usernameElement.ValueKind == JsonValueKind.String)
        {
            username = usernameElement.GetString();
        }

        return new TelegramMessage(chatId, messageId, text, username);
    }

    private static IReadOnlyList<string> SplitTelegramText(string text)
    {
        const int max = 3900;
        text = string.IsNullOrWhiteSpace(text) ? "(empty response)" : text.Trim();
        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += max)
            chunks.Add(text.Substring(i, Math.Min(max, text.Length - i)));
        return chunks;
    }

    private static bool IsAllowedChat(string chatId)
    {
        var allowed = ReadAllowedChatIds();
        return allowed.Contains(NormalizeChatId(chatId), StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ReadAllowedChatIds() =>
        (Environment.GetEnvironmentVariable("TELEGRAM_ALLOWED_CHAT_IDS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeChatId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeChatId(string value) => (value ?? string.Empty).Trim();
    private static string? ReadToken() => Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim();
    private static string RequireToken() => ReadToken() is { Length: > 0 } token ? token : throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is required.");
    private static bool ReadBool(string name, bool fallback) => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    private static int ReadInt(string name, int fallback, int min, int max) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed record TelegramBotStatus(
    bool Enabled,
    bool TokenConfigured,
    int AllowedChatCount,
    long? NextOffset);

public sealed record TelegramSendResult(string ChatId, bool Sent);

internal sealed record TelegramUpdate(
    long UpdateId,
    TelegramMessage? Message,
    TelegramMessage? ChannelPost);

internal sealed record TelegramMessage(
    long ChatId,
    int MessageId,
    string? Text,
    string? FromUsername);
