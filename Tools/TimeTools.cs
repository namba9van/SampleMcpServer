using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

/// <summary>
/// Предоставляет текущие данные о дате и времени из системных часов как MCP-инструменты.
/// </summary>
public class TimeTools
{
    /// <summary>
    /// Возвращает текущее локальное время, время UTC, смещение часового пояса и временную метку ISO 8601.
    /// </summary>
    /// <returns>Отформатированная строка, содержащая текущее локальное время, время UTC, смещение часового пояса и временную метку ISO 8601.</returns>
    [McpServerTool, Description("Возвращает текущее локальное время и время UTC по системным часам, включая смещение часового пояса и временную метку ISO 8601. Используйте этот инструмент вместо предположений о текущем времени.")]
    public static string GetCurrentTime()
    {
        var local = DateTimeOffset.Now;
        var utc = DateTimeOffset.UtcNow;
        var offset = local.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", string.Empty, StringComparison.Ordinal);

        return string.Join(
            Environment.NewLine,
            $"Локальное время: {local:yyyy-MM-dd HH:mm:ss}",
            $"UTC: {utc:yyyy-MM-dd HH:mm:ss}",
            $"Смещение часового пояса: {offset}",
            $"ISO 8601: {local.ToString("yyyy-MM-dd\'T\'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Возвращает текущую локальную календарную дату в форматах ISO и день-месяц-год.
    /// </summary>
    /// <returns>Отформатированная строка, содержащая текущую дату в формате ISO, формате день-месяц-год и день недели в текущей системной культуре.</returns>
    [McpServerTool, Description("Возвращает текущую локальную дату в формате ISO, формате день-месяц-год и с указанием дня недели.")]
    public static string GetCurrentDate()
    {
        var local = DateTimeOffset.Now;

        return string.Join(
            Environment.NewLine,
            $"Дата: {local:yyyy-MM-dd}",
            $"Отформатированная дата: {local:dd.MM.yyyy}",
            $"День недели: {local.ToString("dddd", CultureInfo.CurrentCulture)}");
    }

    /// <summary>
    /// Возвращает текущую локальную временную метку в формате ISO 8601 со смещением часового пояса.
    /// </summary>
    /// <returns>Текущая локальная временная метка в формате ISO 8601.</returns>
    [McpServerTool, Description("Возвращает текущую временную метку локальной машины в формате ISO 8601, включая смещение часового пояса. Используйте, когда требуется точная текущая временная метка.")]
    public static string GetCurrentTimestamp()
    {
        return DateTimeOffset.Now.ToString("yyyy-MM-dd\'T\'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
    }
}
