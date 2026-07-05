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

    /// <summary>Total items under blocked subfolders across all roots (capped per root); a floor when <see cref="BlockedItemsCapped"/>.</summary>
    int BlockedItems => 0;

    /// <summary>True when any root's blocked-item count hit its cap, so <see cref="BlockedItems"/> is a lower bound.</summary>
    bool BlockedItemsCapped => false;

    void AddRoot(CrawlResult result);
    void RemoveRoot(FileNode root);

    /// <summary>Swaps an existing root's crawl for a freshly recrawled one (used by Refresh).</summary>
    void ReplaceRoot(FileNode oldRoot, CrawlResult newResult);
}
