using System.Globalization;

namespace Attendance.Api.Services;

/// <summary>"Today" in the church's local time zone, not the server's (containers run in UTC).</summary>
public class AppClock(IConfiguration config)
{
    readonly TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(config["App:TimeZone"] ?? "Asia/Taipei");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
}

public static class NameOrder
{
    /// <summary>Traditional Chinese order (by stroke count) for people's names.</summary>
    public static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("zh-TW"), ignoreCase: true);
}
