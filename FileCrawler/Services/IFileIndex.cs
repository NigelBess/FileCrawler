using System.Collections.Generic;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>
/// In-memory store of crawled watched roots plus a combined, immutable flat snapshot of every node for search.
/// </summary>
public interface IFileIndex
{
    /// <summary>The watched-root nodes currently held (one per watched folder).</summary>
    IReadOnlyList<FileNode> Roots { get; }

    /// <summary>An immutable snapshot of every node across all roots, used by search. Lock-free to read.</summary>
    IReadOnlyList<FileNode> AllNodes { get; }

    void AddRoot(CrawlResult result);
    void RemoveRoot(FileNode root);

    /// <summary>Swaps an existing root's crawl for a freshly recrawled one (used by Refresh).</summary>
    void ReplaceRoot(FileNode oldRoot, CrawlResult newResult);
}
