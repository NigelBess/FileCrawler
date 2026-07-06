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
}
