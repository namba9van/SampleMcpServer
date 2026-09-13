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
    [Description("Sends a Telegram message to an allow-listed chat using TELEGRAM_BOT_TOKEN.")]
    public async Task<string> TelegramSendMessage(
        [Description("Allowed Telegram chat ID.")] string chatId,
        [Description("Message text. Long text is split into Telegram-sized chunks.")] string text,
        TelegramBotService telegram = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await telegram.SendMessageAsync(chatId, text, cancellationToken),
            JsonOptions);
}
