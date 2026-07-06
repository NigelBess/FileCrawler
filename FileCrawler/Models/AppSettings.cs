using System;
using System.Text.Json.Serialization;

namespace FileCrawler.Models;

/// <summary>User-configurable application settings, persisted via <see cref="Services.ISettingsStore"/>.</summary>
public sealed record AppSettings
{
    /// <summary>Default per-folder crawl time limit, in seconds.</summary>
    public const double DefaultMaxCrawlSeconds = 10;

    /// <summary>
    /// Maximum time to spend crawling a single watched folder before stopping and keeping whatever was found so
    /// far. A non-positive value means no limit.
    /// </summary>
    public double MaxCrawlSeconds { get; init; } = DefaultMaxCrawlSeconds;

    /// <summary>The crawl time limit as a <see cref="TimeSpan"/>, or null when unlimited (a non-positive value).</summary>
    [JsonIgnore]
    public TimeSpan? MaxCrawlTime => MaxCrawlSeconds > 0 ? TimeSpan.FromSeconds(MaxCrawlSeconds) : null;

    /// <summary>Whether the watched-folders sidebar is expanded. Persisted so the layout survives restarts.</summary>
    public bool SidebarExpanded { get; init; }

    /// <summary>Whether the filter bar is expanded. Defaults to expanded.</summary>
    public bool FiltersExpanded { get; init; } = true;

    /// <summary>The result column the list is sorted by, stored as the <c>ResultSortColumn</c> name.</summary>
    public string SortColumn { get; init; } = "Name";

    /// <summary>Whether the current sort is descending.</summary>
    public bool SortDescending { get; init; }
}
