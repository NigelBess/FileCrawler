using System;
using FileCrawler.Utilities;
using Xunit;

namespace FileCrawler.Tests;

public class DatePresetTests
{
    // A fixed local "now": Saturday 2026-07-04 15:30 local time.
    private static readonly DateTime NowLocal = new(2026, 7, 4, 15, 30, 0);

    private static DateTime LocalMidnightUtc(int year, int month, int day) =>
        DateTime.SpecifyKind(new DateTime(year, month, day), DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void Any_has_no_bounds()
    {
        var (after, before) = DatePresets.ToUtcRange(DatePreset.Any, null, null, NowLocal);
        Assert.Null(after);
        Assert.Null(before);
    }

    [Fact]
    public void Today_starts_at_local_midnight()
    {
        var (after, before) = DatePresets.ToUtcRange(DatePreset.Today, null, null, NowLocal);
        Assert.Equal(LocalMidnightUtc(2026, 7, 4), after);
        Assert.Null(before);
    }

    [Fact]
    public void Last7Days_starts_seven_days_back()
    {
        var (after, _) = DatePresets.ToUtcRange(DatePreset.Last7Days, null, null, NowLocal);
        Assert.Equal(LocalMidnightUtc(2026, 6, 27), after);
    }

    [Fact]
    public void Last30Days_starts_thirty_days_back()
    {
        var (after, _) = DatePresets.ToUtcRange(DatePreset.Last30Days, null, null, NowLocal);
        Assert.Equal(LocalMidnightUtc(2026, 6, 4), after);
    }

    [Fact]
    public void ThisYear_starts_january_first()
    {
        var (after, _) = DatePresets.ToUtcRange(DatePreset.ThisYear, null, null, NowLocal);
        Assert.Equal(LocalMidnightUtc(2026, 1, 1), after);
    }

    [Fact]
    public void Custom_to_date_includes_its_whole_day()
    {
        var (after, before) = DatePresets.ToUtcRange(
            DatePreset.Custom, new DateTime(2026, 3, 1), new DateTime(2026, 3, 15), NowLocal);

        Assert.Equal(LocalMidnightUtc(2026, 3, 1), after);
        Assert.Equal(LocalMidnightUtc(2026, 3, 16), before); // exclusive bound = midnight after the "to" day
    }

    [Fact]
    public void Custom_with_only_one_bound_leaves_the_other_open()
    {
        var (after, before) = DatePresets.ToUtcRange(DatePreset.Custom, null, new DateTime(2026, 3, 15), NowLocal);
        Assert.Null(after);
        Assert.NotNull(before);
    }
}
