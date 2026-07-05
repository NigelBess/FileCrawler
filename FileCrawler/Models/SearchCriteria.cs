using System;
using System.Collections.Generic;

namespace FileCrawler.Models;

/// <summary>
/// Everything the search service needs for one search: the text query plus optional structured filters.
/// </summary>
/// <param name="Query">Forgiving name query; may be empty when structured filters are active ("browse mode").</param>
/// <param name="Extensions">
/// File-extension allowlist, lowercase with leading dot (".png"). <c>null</c> means no file-type restriction
/// (every file matches); a non-empty list restricts files to those extensions; an <em>empty</em> list matches
/// no files at all (e.g. "folders only", or the user unchecking every type).
/// </param>
/// <param name="MinSizeBytes">Inclusive lower size bound; null means unbounded.</param>
/// <param name="MaxSizeBytes">Inclusive upper size bound; null means unbounded.</param>
/// <param name="ModifiedAfterUtc">Inclusive lower bound on last write time (UTC).</param>
/// <param name="ModifiedBeforeUtc">Exclusive upper bound on last write time (UTC).</param>
/// <param name="IncludeFolders">Whether folders may appear in results; independent of the extension allowlist.</param>
/// <param name="BlockedPaths">
/// Folders (normalized absolute paths) excluded from this search only — the folder and everything under it is
/// dropped at search time, without recrawling. Distinct from a watched-folder block, which is never indexed at
/// all. <c>null</c> or empty means no per-search exclusions.
/// </param>
public sealed record SearchCriteria(
    string Query,
    IReadOnlyCollection<string>? Extensions = null,
    long? MinSizeBytes = null,
    long? MaxSizeBytes = null,
    DateTime? ModifiedAfterUtc = null,
    DateTime? ModifiedBeforeUtc = null,
    bool IncludeFolders = true,
    IReadOnlyCollection<string>? BlockedPaths = null)
{
    /// <summary>True when any structured filter (beyond the text query) is active.</summary>
    public bool HasFilters =>
        Extensions is not null          // any file-type restriction — an allowlist, or (when empty) "no files"
        || !IncludeFolders
        || MinSizeBytes.HasValue
        || MaxSizeBytes.HasValue
        || ModifiedAfterUtc.HasValue
        || ModifiedBeforeUtc.HasValue
        || BlockedPaths is { Count: > 0 };

    /// <summary>True when there is nothing to search by at all.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Query) && !HasFilters;

    /// <summary>
    /// True when the filters exclude every possible node — no file types are allowed <em>and</em> folders are
    /// hidden — so the search returns nothing regardless of the query (e.g. "Select none"). Distinct from a
    /// query that merely happens to have no matches.
    /// </summary>
    public bool MatchesNothing => Extensions is { Count: 0 } && !IncludeFolders;
}
