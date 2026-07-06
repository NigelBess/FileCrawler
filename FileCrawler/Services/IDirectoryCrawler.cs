using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>Crawls a directory tree into an in-memory <see cref="CrawlResult"/>.</summary>
public interface IDirectoryCrawler
{
    /// <summary>
    /// Recursively crawls <paramref name="rootPath"/>, building a <see cref="FileNode"/> tree with aggregated
    /// folder sizes and a flattened node list for search. Any directory whose normalized absolute path is in
    /// <paramref name="blockedFolders"/> is skipped entirely — neither it nor its contents are included.
    /// When <paramref name="maxCrawlTime"/> is set (positive), the crawl stops once it elapses and returns
    /// whatever was gathered so far with <see cref="CrawlResult.TimedOut"/> set; null means no time limit.
    /// </summary>
    Task<CrawlResult> CrawlAsync(
        string rootPath,
        IReadOnlySet<string>? blockedFolders,
        IProgress<CrawlProgress>? progress,
        CancellationToken ct,
        TimeSpan? maxCrawlTime = null);
}
