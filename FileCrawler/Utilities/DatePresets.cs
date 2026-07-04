using System;

namespace FileCrawler.Utilities;

/// <summary>Quick choices for the "Modified" filter.</summary>
public enum DatePreset { Any, Today, Last7Days, Last30Days, ThisYear, Custom }

/// <summary>Converts a <see cref="DatePreset"/> (plus optional custom range) into a UTC window.</summary>
public static class DatePresets
{
    /// <summary>
    /// Returns the UTC window for <paramref name="preset"/>: after is inclusive, before is exclusive.
    /// Boundaries are computed from local calendar days (Today = local midnight) so they match what a user
    /// sees in Explorer. <paramref name="customTo"/> is inclusive of its whole day.
    /// </summary>
    /// <param name="nowLocal">Current local time, injectable for tests.</param>
    public static (DateTime? AfterUtc, DateTime? BeforeUtc) ToUtcRange(
        DatePreset preset, DateTime? customFrom, DateTime? customTo, DateTime nowLocal)
    {
        var today = nowLocal.Date;
        return preset switch
        {
            DatePreset.Any => (null, null),
            DatePreset.Today => (ToUtc(today), null),
            DatePreset.Last7Days => (ToUtc(today.AddDays(-7)), null),
            DatePreset.Last30Days => (ToUtc(today.AddDays(-30)), null),
            DatePreset.ThisYear => (ToUtc(new DateTime(today.Year, 1, 1)), null),
            DatePreset.Custom => (
                customFrom is { } from ? ToUtc(from.Date) : null,
                customTo is { } to ? ToUtc(to.Date.AddDays(1)) : null),
            _ => (null, null),
        };
    }

    private static DateTime ToUtc(DateTime localDate) =>
        DateTime.SpecifyKind(localDate, DateTimeKind.Local).ToUniversalTime();
}
