using System.ComponentModel;
using ModelContextProtocol.Server;

public sealed class TimeTools
{
    [McpServerTool(Name = "time_now")]
    [Description("Returns the current UTC time, or the current time in a specified IANA/Windows time zone.")]
    public string Now([Description("Optional time-zone ID. Leave empty for UTC.")] string? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
            return DateTimeOffset.UtcNow.ToString("O");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).ToString("O");
    }
}
