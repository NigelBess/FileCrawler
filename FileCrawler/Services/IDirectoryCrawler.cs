using System;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>Crawls a directory tree into an in-memory <see cref="CrawlResult"/>.</summary>
public interface IDirectoryCrawler
{
    /// <summary>
    /// Recursively crawls <paramref name="rootPath"/>, building a <see cref="FileNode"/> tree with aggregated
    /// folder sizes and a flattened node list for search.
    /// </summary>
    Task<CrawlResult> CrawlAsync(string rootPath, IProgress<CrawlProgress>? progress, CancellationToken ct);
}
