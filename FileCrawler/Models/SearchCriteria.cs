using System;
using System.Collections.Generic;

namespace FileCrawler.Models;

/// <summary>Restricts search results to files, folders, or both.</summary>
public enum NodeKindFilter { All, FilesOnly, FoldersOnly }

/// <summary>
/// Everything the search service needs for one search: the text query plus optional structured filters.
/// </summary>
/// <param name="Query">Forgiving name query; may be empty when structured filters are active ("browse mode").</param>
/// <param name="Extensions">Extensions to include, lowercase with leading dot (".png"); null or empty means no extension filter.</param>
/// <param name="MinSizeBytes">Inclusive lower size bound; null means unbounded.</param>
/// <param name="MaxSizeBytes">Inclusive upper size bound; null means unbounded.</param>
/// <param name="ModifiedAfterUtc">Inclusive lower bound on last write time (UTC).</param>
/// <param name="ModifiedBeforeUtc">Exclusive upper bound on last write time (UTC).</param>
/// <param name="Kind">Restricts results to files, folders, or both.</param>
public sealed record SearchCriteria(
    string Query,
    IReadOnlyCollection<string>? Extensions = null,
    long? MinSizeBytes = null,
    long? MaxSizeBytes = null,
    DateTime? ModifiedAfterUtc = null,
    DateTime? ModifiedBeforeUtc = null,
    NodeKindFilter Kind = NodeKindFilter.All)
{
    /// <summary>True when any structured filter (beyond the text query) is active.</summary>
    public bool HasFilters =>
        Extensions is { Count: > 0 }
        || MinSizeBytes.HasValue
        || MaxSizeBytes.HasValue
        || ModifiedAfterUtc.HasValue
        || ModifiedBeforeUtc.HasValue
        || Kind != NodeKindFilter.All;

    /// <summary>True when there is nothing to search by at all.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Query) && !HasFilters;
}
