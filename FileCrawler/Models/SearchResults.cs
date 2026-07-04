using System;
using System.Collections.Generic;

namespace FileCrawler.Models;

/// <summary>
/// The outcome of a search: the matching nodes (already capped), how many nodes were scanned, and whether
/// the result set hit the cap (so the UI can show "showing first N of many").
/// </summary>
public sealed record SearchResults(IReadOnlyList<FileNode> Items, int Scanned, bool Capped)
{
    public static readonly SearchResults Empty = new(Array.Empty<FileNode>(), 0, false);
}
