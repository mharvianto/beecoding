using Xunit;
using BeeCoding.Services;

namespace BeeCoding.Tests;

public class TimeBucketingTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 13, 45, 0, DateTimeKind.Utc);   // a Wednesday

    [Fact]
    public void Hour_window_is_the_last_n_whole_hours_ending_at_the_current_one()
    {
        var w = TimeBucketing.Window("hour", 48, Now);
        Assert.Equal(48, w.Count);
        Assert.Equal(new DateTime(2026, 11, 4, 13, 0, 0, DateTimeKind.Utc), w[^1]);
        Assert.Equal(new DateTime(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc), w[0]);
    }

    [Fact]
    public void Day_window_has_no_gaps()
    {
        var w = TimeBucketing.Window("day", 14, Now);
        Assert.Equal(new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), w[^1]);
        Assert.Equal(new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc), w[0]);
        Assert.All(w.Zip(w.Skip(1)), p => Assert.Equal(TimeSpan.FromDays(1), p.Second - p.First));
    }

    [Fact]
    public void Week_window_starts_on_mondays()
    {
        var w = TimeBucketing.DayWindow("week", 12, Now);
        Assert.Equal(new DateOnly(2026, 11, 2), w[^1]);
        Assert.All(w, d => Assert.Equal(DayOfWeek.Monday, d.DayOfWeek));
    }
}
