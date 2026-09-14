namespace BeeCoding.Services;

/// <summary>Shared hour/day/week bucketing for trend charts — used by both the admin
/// dashboard and the personal account dashboard. Only for sources with a precise per-event
/// CreatedAt; a day-only source (like AiUsage) buckets on DateOnly separately and skips
/// "hour" entirely.</summary>
public static class TimeBucketing
{
    public static string NormalizeGranularity(string? g) =>
        (g ?? "week").Trim().ToLowerInvariant() is "hour" or "day" ? g!.Trim().ToLowerInvariant() : "week";

    public static DateTime BucketStart(DateTime dt, string granularity) => granularity switch
    {
        "hour" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc),
        "day" => new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc),
        _ => WeekStartUtc(dt),
    };

    public static DateTime WeekStartUtc(DateTime dt)
    {
        var d = DateOnly.FromDateTime(dt);
        var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        return monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    public static string FormatPeriodStart(DateTime dt, string granularity) =>
        granularity == "hour" ? dt.ToString("o") : dt.ToString("yyyy-MM-dd");
}
