using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Optional Telegram polling bridge. It turns messages from explicitly allowed chats
/// into durable agent jobs and sends the final result back through the notification pipeline.
/// With TELEGRAM_DELIVERY=inbox the messages are stored in a durable inbox instead, so a connected MCP client
/// (an interactive Claude session) can read and answer them itself via telegram_messages_wait / telegram_send_message.
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
    private string? _botUsername;
    private long? _botId;
    private readonly Channel<long> _inboxSignal = Channel.CreateUnbounded<long>();
    private readonly SemaphoreSlim _inboxInit = new(1, 1);
    private bool _inboxReady;

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
        return new TelegramBotStatus(enabled, tokenConfigured, allowedChats.Count, _nextOffset, ReadDelivery());
    }

    public async Task<TelegramSendResult> SendMessageAsync(
        string chatId,
        string text,
        int? replyToMessageId = null,
        CancellationToken ct = default)
    {
        var token = RequireToken();
        chatId = NormalizeChatId(chatId);
        if (!IsAllowedChat(chatId))
            throw new UnauthorizedAccessException($"Telegram chat '{chatId}' is not allow-listed.");

        var ids = await SendTelegramMessageAsync(token, chatId, text, ct, replyToMessageId);
        return new TelegramSendResult(chatId, ids.Count > 0, ids.Count > 0 ? ids[0] : null, ids);
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

        // The MCP host may start several server processes with the same configuration (Claude Desktop does: one per
        // window/session). Telegram allows a single getUpdates consumer per token, so only the process holding the
        // poll lock polls; the others wait and take over when the holder exits. Inbox/queue live in the shared SQLite.
        await _memory.InitializeAsync(stoppingToken);
        var lockPath = _memory.DatabasePath + ".telegram-poll.lock";
        var announcedWaiting = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            FileStream? pollLock;
            try { pollLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException)
            {
                if (!announcedWaiting) { _logger.LogInformation("Telegram bot bridge: another server process is polling; this one is standing by."); announcedWaiting = true; }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                if (!announcedWaiting) { _logger.LogInformation("Telegram bot bridge: another server process is polling; this one is standing by."); announcedWaiting = true; }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            using (pollLock)
            {
                announcedWaiting = false;
                _logger.LogInformation("Telegram bot bridge started (poll lock {LockPath}).", lockPath);
                try
                {
                    if (ReadBool("TELEGRAM_DROP_PENDING_UPDATES", true))
                        await DropPendingUpdatesAsync(token, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Telegram bot could not drop pending updates."); }

                await PollLoopAsync(token, stoppingToken);
            }
        }
    }

    private async Task PollLoopAsync(string token, CancellationToken stoppingToken)
    {
        var conflicts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(token, stoppingToken);
                conflicts = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Another consumer (an instance without the lock, or one on another machine) is polling this token.
                conflicts++;
                var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * conflicts));
                _logger.LogWarning("Telegram getUpdates conflict (409): another bot instance uses this token. Retrying in {Delay}s.", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
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
        await EnsureBotIdentityAsync(token, ct);
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
        var addressing = ClassifyAddressing(message);
        if (ReadInboxFilter() == "addressed" && addressing is "chat")
        {
            await _runtime.AuditAsync("telegram", "message", "ignored", new { update.UpdateId, chatId, message.MessageId, reason = "not_addressed" }, ct);
            return;
        }

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            await SendTelegramMessageAsync(
                token,
                chatId,
                "Send a task message and I will queue it for the local Agent Host or the connected Claude session. Use /status to check that the bridge is alive.",
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

        var delivery = ReadDelivery();
        if (delivery is "inbox" or "both")
        {
            var inboxId = await StoreInboxAsync(chatId, message, update.UpdateId, text, addressing, ct);
            await _runtime.AuditAsync("telegram", "inbox", "ok", new { update.UpdateId, chatId, message.MessageId, inboxId }, ct);
            if (delivery == "inbox") return;
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
                Addressing = addressing,
                message.ReplyToMessageId,
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

    // ----- Inbox (TELEGRAM_DELIVERY=inbox|both) -----

    private static string ReadDelivery()
    {
        var value = (Environment.GetEnvironmentVariable("TELEGRAM_DELIVERY") ?? "queue").Trim().ToLowerInvariant();
        return value is "inbox" or "both" ? value : "queue";
    }

    private SqliteConnection OpenDb() => new($"Data Source={_memory.DatabasePath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True");

    private async Task EnsureInboxAsync(CancellationToken ct)
    {
        if (_inboxReady) return;
        await _inboxInit.WaitAsync(ct);
        try
        {
            if (_inboxReady) return;
            await _memory.InitializeAsync(ct);
            await using var db = OpenDb();
            await db.OpenAsync(ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS telegram_inbox (
                    inbox_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    update_id INTEGER NOT NULL UNIQUE,
                    chat_id TEXT NOT NULL,
                    message_id INTEGER NOT NULL,
                    from_username TEXT NULL,
                    text TEXT NOT NULL,
                    received_utc TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'new',
                    acked_utc TEXT NULL,
                    addressing TEXT NOT NULL DEFAULT 'chat',
                    reply_to_message_id INTEGER NULL,
                    reply_to_from TEXT NULL,
                    reply_to_text TEXT NULL,
                    thread_id INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS ix_telegram_inbox_status ON telegram_inbox(status, inbox_id);
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            foreach (var column in new[] { "addressing TEXT NOT NULL DEFAULT 'chat'", "reply_to_message_id INTEGER NULL", "reply_to_from TEXT NULL", "reply_to_text TEXT NULL", "thread_id INTEGER NULL" })
            {
                await using var alter = db.CreateCommand();
                alter.CommandText = $"ALTER TABLE telegram_inbox ADD COLUMN {column};";
                try { await alter.ExecuteNonQueryAsync(ct); } catch (SqliteException) { /* column exists */ }
            }
            _inboxReady = true;
        }
        finally { _inboxInit.Release(); }
    }

    private async Task<long> StoreInboxAsync(string chatId, TelegramMessage message, long updateId, string text, string addressing, CancellationToken ct)
    {
        await EnsureInboxAsync(ct);
        await using var db = OpenDb();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO telegram_inbox (update_id, chat_id, message_id, from_username, text, received_utc, status, addressing, reply_to_message_id, reply_to_from, reply_to_text, thread_id)
            VALUES ($update, $chat, $message, $from, $text, $received, 'new', $addressing, $replyId, $replyFrom, $replyText, $thread);
            SELECT inbox_id FROM telegram_inbox WHERE update_id = $update;
            """;
        cmd.Parameters.AddWithValue("$update", updateId);
        cmd.Parameters.AddWithValue("$chat", chatId);
        cmd.Parameters.AddWithValue("$message", message.MessageId);
        cmd.Parameters.AddWithValue("$from", (object?)message.FromUsername ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$received", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$addressing", addressing);
        cmd.Parameters.AddWithValue("$replyId", (object?)message.ReplyToMessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$replyFrom", (object?)message.ReplyToFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$replyText", (object?)message.ReplyToText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thread", (object?)message.ThreadId ?? DBNull.Value);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        _inboxSignal.Writer.TryWrite(id);
        return id;
    }

    /// <summary>Returns inbox messages with inbox_id greater than <paramref name="afterId"/>, optionally filtered by status (new|acked).</summary>
    public async Task<IReadOnlyList<TelegramInboxMessage>> PollInboxAsync(long afterId = 0, string? status = "new", int limit = 50, CancellationToken ct = default)
    {
        await EnsureInboxAsync(ct);
        limit = Math.Clamp(limit, 1, 500);
        await using var db = OpenDb();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT inbox_id, update_id, chat_id, message_id, from_username, text, received_utc, status, acked_utc,
                   addressing, reply_to_message_id, reply_to_from, reply_to_text, thread_id
            FROM telegram_inbox
            WHERE inbox_id > $after AND ($status IS NULL OR status = $status)
            ORDER BY inbox_id
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$after", afterId);
        cmd.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$limit", limit);
        var result = new List<TelegramInboxMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new TelegramInboxMessage(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13)));
        }
        return result;
    }

    /// <summary>Long-polls the inbox: returns immediately when matching messages exist, otherwise waits up to timeoutSeconds for a new one.</summary>
    public async Task<IReadOnlyList<TelegramInboxMessage>> WaitInboxAsync(long afterId = 0, string? status = "new", int timeoutSeconds = 30, int limit = 50, CancellationToken ct = default)
    {
        var existing = await PollInboxAsync(afterId, status, limit, ct);
        if (existing.Count > 0) return existing;
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 300);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            while (await _inboxSignal.Reader.WaitToReadAsync(timeout.Token))
            {
                while (_inboxSignal.Reader.TryRead(out _)) { }
                var messages = await PollInboxAsync(afterId, status, limit, timeout.Token);
                if (messages.Count > 0) return messages;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return Array.Empty<TelegramInboxMessage>();
    }

    /// <summary>Marks inbox messages up to and including <paramref name="upToId"/> as acked so they are not returned again.</summary>
    public async Task<int> AckInboxAsync(long upToId, CancellationToken ct = default)
    {
        await EnsureInboxAsync(ct);
        await using var db = OpenDb();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE telegram_inbox SET status = 'acked', acked_utc = $now WHERE status = 'new' AND inbox_id <= $id;";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", upToId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string ReadInboxFilter()
        => (Environment.GetEnvironmentVariable("TELEGRAM_INBOX_FILTER") ?? "all").Trim().ToLowerInvariant() == "addressed" ? "addressed" : "all";

    /// <summary>chat — plain message to the chat; reply_to_bot — reply to one of the bot's messages; mention — the bot is @mentioned;
    /// reply — reply to another participant's message (the bot is not addressed).</summary>
    private string ClassifyAddressing(TelegramMessage message)
    {
        var botName = _botUsername ?? "";
        if (message.ReplyToMessageId is > 0)
        {
            var toBot = (_botId is { } id && message.ReplyToFromId == id) ||
                        (botName.Length > 0 && string.Equals(message.ReplyToFrom, botName, StringComparison.OrdinalIgnoreCase));
            if (toBot) return "reply_to_bot";
        }
        if (botName.Length > 0 && message.Mentions.Any(m => string.Equals(m, botName, StringComparison.OrdinalIgnoreCase))) return "mention";
        if (_botId is { } bid && message.MentionedUserIds.Contains(bid)) return "mention";
        return message.ReplyToMessageId is > 0 ? "reply" : "chat";
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

    private async Task<IReadOnlyList<int>> SendTelegramMessageAsync(string token, string chatId, string text, CancellationToken ct, int? replyToMessageId = null)
    {
        var chunks = SplitTelegramText(text);
        var sentIds = new List<int>();
        var first = true;
        foreach (var chunk in chunks)
        {
            object payload = first && replyToMessageId is > 0
                ? new { chat_id = chatId, text = chunk, disable_web_page_preview = true, reply_parameters = new { message_id = replyToMessageId.Value, allow_sending_without_reply = true } }
                : new { chat_id = chatId, text = chunk, disable_web_page_preview = true };
            first = false;

            using var request = CreateTelegramRequest(token, "sendMessage", payload);
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            response.EnsureSuccessStatusCode();
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("message_id", out var id) && id.TryGetInt32(out var messageId))
                    sentIds.Add(messageId);
            }
            catch (JsonException) { /* message was sent; id is informational */ }
        }

        return sentIds;
    }

    /// <summary>Resolves the bot's own username/id once (getMe) so replies and mentions addressed to the bot can be recognised.</summary>
    private async Task EnsureBotIdentityAsync(string token, CancellationToken ct)
    {
        if (_botUsername is not null) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.telegram.org/bot{token}/getMe"));
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var result = doc.RootElement.GetProperty("result");
            _botUsername = result.TryGetProperty("username", out var u) ? u.GetString() : "";
            _botId = result.TryGetProperty("id", out var i) && i.TryGetInt64(out var id) ? id : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram getMe failed; addressing detection will rely on reply_to only.");
            _botUsername = "";
        }
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

        int? replyToId = null; string? replyToFrom = null; long? replyToFromId = null; string? replyToText = null;
        if (message.TryGetProperty("reply_to_message", out var reply) && reply.ValueKind == JsonValueKind.Object)
        {
            if (reply.TryGetProperty("message_id", out var rid) && rid.TryGetInt32(out var r)) replyToId = r;
            if (reply.TryGetProperty("from", out var rfrom))
            {
                if (rfrom.TryGetProperty("username", out var ru) && ru.ValueKind == JsonValueKind.String) replyToFrom = ru.GetString();
                if (rfrom.TryGetProperty("id", out var rfid) && rfid.TryGetInt64(out var rf)) replyToFromId = rf;
            }
            if (reply.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String)
            {
                var full = rt.GetString() ?? "";
                replyToText = full.Length > 300 ? full[..300] + "…" : full;
            }
        }

        var mentions = new List<string>(); var mentionedIds = new List<long>();
        if (text is not null && message.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in entities.EnumerateArray())
            {
                var type = entity.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "mention" && entity.TryGetProperty("offset", out var o) && entity.TryGetProperty("length", out var l))
                {
                    var offset = o.GetInt32(); var length = l.GetInt32();
                    if (offset >= 0 && length > 1 && offset + length <= text.Length) mentions.Add(text.Substring(offset + 1, length - 1));
                }
                else if (type == "text_mention" && entity.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var uid) && uid.TryGetInt64(out var mid))
                    mentionedIds.Add(mid);
            }
        }

        int? threadId = message.TryGetProperty("message_thread_id", out var th) && th.TryGetInt32(out var thread) ? thread : null;
        return new TelegramMessage(chatId, messageId, text, username, replyToId, replyToFrom, replyToFromId, replyToText, mentions, mentionedIds, threadId);
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
    long? NextOffset,
    string Delivery);

public sealed record TelegramInboxMessage(
    long InboxId,
    long UpdateId,
    string ChatId,
    int MessageId,
    string? FromUsername,
    string Text,
    string ReceivedUtc,
    string Status,
    string? AckedUtc,
    string Addressing,
    int? ReplyToMessageId,
    string? ReplyToFrom,
    string? ReplyToText,
    int? ThreadId);

public sealed record TelegramSendResult(string ChatId, bool Sent, int? MessageId, IReadOnlyList<int> MessageIds);

internal sealed record TelegramUpdate(
    long UpdateId,
    TelegramMessage? Message,
    TelegramMessage? ChannelPost);

internal sealed record TelegramMessage(
    long ChatId,
    int MessageId,
    string? Text,
    string? FromUsername,
    int? ReplyToMessageId = null,
    string? ReplyToFrom = null,
    long? ReplyToFromId = null,
    string? ReplyToText = null,
    IReadOnlyList<string>? MentionsOrNull = null,
    IReadOnlyList<long>? MentionedUserIdsOrNull = null,
    int? ThreadId = null)
{
    public IReadOnlyList<string> Mentions => MentionsOrNull ?? Array.Empty<string>();
    public IReadOnlyList<long> MentionedUserIds => MentionedUserIdsOrNull ?? Array.Empty<long>();
}
