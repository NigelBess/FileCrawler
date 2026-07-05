using System.Collections.Generic;

namespace FileCrawler.Models;

/// <summary>
/// The output of crawling a single watched root: the tree, a flattened list of every node for search,
/// a count of entries that were skipped (inaccessible, etc.), and a (capped) count of items living under
/// this root's blocked subfolders — content deliberately excluded from the index but surfaced to the user
/// so they know results are incomplete. <see cref="BlockedItemsCapped"/> marks the count as a lower bound.
/// </summary>
public sealed record CrawlResult(
    FileNode Root,
    IReadOnlyList<FileNode> AllNodes,
    int Skipped,
    int BlockedItems = 0,
    bool BlockedItemsCapped = false);
