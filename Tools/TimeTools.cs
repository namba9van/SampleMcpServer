using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

/// <summary>
/// Provides current local date and time information from the system clock as MCP tools.
/// </summary>
public class TimeTools
{
    /// <summary>
    /// Returns the current local time, UTC time, local time-zone offset, and ISO 8601 timestamp.
    /// </summary>
    /// <returns>A formatted string containing the current local time, UTC time, time-zone offset, and ISO 8601 timestamp.</returns>
    [McpServerTool, Description("Returns the current local time and UTC time from the system clock, including the local time-zone offset and an ISO 8601 timestamp. Use this tool instead of guessing the current time.")]
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
    /// Returns the current local calendar date in ISO and day-month-year formats.
    /// </summary>
    /// <returns>A formatted string containing the current date in ISO format, a day-month-year format, and the day of the week in the current system culture.</returns>
    [McpServerTool, Description("Returns today's local date, including ISO format, day-month-year format, and the day of the week.")]
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
    /// Returns the current local timestamp in ISO 8601 format with the local time-zone offset.
    /// </summary>
    /// <returns>The current local timestamp in ISO 8601 format.</returns>
    [McpServerTool, Description("Returns the current local machine timestamp in ISO 8601 format, including the local time-zone offset. Use when an exact current timestamp is required.")]
    public static string GetCurrentTimestamp()
    {
        return DateTimeOffset.Now.ToString("yyyy-MM-dd\'T\'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
    }
}
