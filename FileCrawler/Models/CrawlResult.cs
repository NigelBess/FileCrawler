using System.Collections.Generic;

namespace FileCrawler.Models;

/// <summary>
/// The output of crawling a single watched root: the tree, a flattened list of every node for search,
/// and a count of entries that were skipped (inaccessible, etc.).
/// </summary>
public sealed record CrawlResult(FileNode Root, IReadOnlyList<FileNode> AllNodes, int Skipped);
