using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

/// <summary>
/// Предоставляет текущий локальный date и time information из системный clock as MCP инструменты.
/// </summary>
public class TimeTools
{
    /// <summary>
    /// Возвращает текущий локальный time, UTC time, локальный time-zодин offset, и ISO 8601 временная метка.
    /// </summary>
    /// <returns>formatted строковый containing текущий локальный time, UTC time, time-zодин offset, и ISO 8601 временная метка.</returns>
    [McpServerTool, Description("Возвращает текущее локальное время и время UTC по системным часам, включая смещение часового пояса и временную метку ISO 8601. Используйте этот инструмент вместо предположений о текущем времени.")]
    public static string GetCurrentTime()
    {
        var local = DateTimeOffset.Now;
        var utc = DateTimeOffset.UtcNow;
        var offset = local.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", string.Empty, StringComparison.Ordinal);

        return string.Join(
            Environment.NewLine,
            $"Local time: {local:yyyy-MM-dd HH:mm:ss}",
            $"UTC: {utc:yyyy-MM-dd HH:mm:ss}",
            $"Timezone offset: {offset}",
            $"ISO 8601: {local.ToString("yyyy-MM-dd\'T\'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Возвращает текущий локальный calendar date в ISO и day-month-year форматы.
    /// </summary>
    /// <returns>formatted строковый containing текущий date в ISO формат, один day-month-year формат, и day из week в текущий системный culture.</returns>
    [McpServerTool, Description("Возвращает текущую локальную дату в формате ISO, формате день-месяц-год и с указанием дня недели.")]
    public static string GetCurrentDate()
    {
        var local = DateTimeOffset.Now;

        return string.Join(
            Environment.NewLine,
            $"Date: {local:yyyy-MM-dd}",
            $"Formatted date: {local:dd.MM.yyyy}",
            $"Day of week: {local.ToString("dddd", CultureInfo.CurrentCulture)}");
    }

    /// <summary>
    /// Возвращает текущий локальный временная метка в ISO 8601 формат с локальный time-zодин offset.
    /// </summary>
    /// <returns>текущий локальный временная метка в ISO 8601 формат.</returns>
    [McpServerTool, Description("Возвращает текущую временную метку локальной машины в формате ISO 8601, включая смещение часового пояса. Используйте, когда требуется точная текущая временная метка.")]
    public static string GetCurrentTimestamp()
    {
        return DateTimeOffset.Now.ToString("yyyy-MM-dd\'T\'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
    }
}
