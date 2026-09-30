using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Services;

public sealed class TelegramTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "telegram_bot_status")]
    [Description("Returns Telegram bot bridge configuration status without exposing the bot token.")]
    public string TelegramBotStatus(TelegramBotService telegram = null!)
        => JsonSerializer.Serialize(telegram.GetStatus(), JsonOptions);

    [McpServerTool(Name = "telegram_send_message")]
    [Description("Sends a Telegram message to an allow-listed chat using TELEGRAM_BOT_TOKEN. Returns the sent message id(s); pass replyToMessageId to answer a specific message as a Telegram reply.")]
    public async Task<string> TelegramSendMessage(
        [Description("Allowed Telegram chat ID.")] string chatId,
        [Description("Message text. Long text is split into Telegram-sized chunks.")] string text,
        [Description("Optional message id to reply to (the first chunk is sent as a reply).")] int? replyToMessageId = null,
        TelegramBotService telegram = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await telegram.SendMessageAsync(chatId, text, replyToMessageId, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "telegram_messages_poll")]
    [Description("Reads messages received from allow-listed Telegram chats into the bridge inbox (TELEGRAM_DELIVERY=inbox|both). Returns immediately; use telegram_messages_wait to block until a new message arrives.")]
    public async Task<string> TelegramMessagesPoll(
        [Description("Return messages with inboxId greater than this value.")] long afterId = 0,
        [Description("Status filter: new (default), acked, or null for all.")] string? status = "new",
        [Description("Maximum messages, 1..500.")] int limit = 50,
        TelegramBotService telegram = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await telegram.PollInboxAsync(afterId, status, limit, cancellationToken), JsonOptions);

    [McpServerTool(Name = "telegram_messages_wait")]
    [Description("Long-polls the Telegram bridge inbox: returns matching messages at once, otherwise waits up to timeoutSeconds for a new message from an allow-listed chat. Each message carries Addressing: chat (plain message), reply_to_bot, mention, or reply (to another participant). Answer with telegram_send_message (replyToMessageId=MessageId for an addressed reply) and acknowledge with telegram_messages_ack.")]
    public async Task<string> TelegramMessagesWait(
        [Description("Return messages with inboxId greater than this value.")] long afterId = 0,
        [Description("Status filter: new (default), acked, or null for all.")] string? status = "new",
        [Description("Maximum wait, 1..300 seconds.")] int timeoutSeconds = 30,
        [Description("Maximum messages, 1..500.")] int limit = 50,
        TelegramBotService telegram = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await telegram.WaitInboxAsync(afterId, status, timeoutSeconds, limit, cancellationToken), JsonOptions);

    [McpServerTool(Name = "telegram_messages_ack")]
    [Description("Marks Telegram inbox messages up to and including upToId as acked so they are not returned as new again.")]
    public async Task<string> TelegramMessagesAck(
        [Description("Highest inboxId to acknowledge.")] long upToId,
        TelegramBotService telegram = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(new { upToId, acked = await telegram.AckInboxAsync(upToId, cancellationToken) }, JsonOptions);
}
