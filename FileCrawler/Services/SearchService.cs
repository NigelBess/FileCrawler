using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;
using FileCrawler.Utilities;

namespace FileCrawler.Services;

/// <summary>
/// Searches the file index by node name and structured filters (extension, size, modified date, folders),
/// reusing <see cref="UserSearchHelpers.FindAllMatches"/> for the forgiving name-match logic. Structured
/// filters are cheap scalar checks and run before name matching to prune the expensive matcher. The scan
/// runs off the UI thread, is cancellable per item, and stops once the result cap is reached — both the
/// filter and <see cref="UserSearchHelpers.FindAllMatches"/> compose lazily, so capping the consumer here
/// is enough to keep it responsive over millions of nodes. An empty query with active filters returns all
/// filter matches ("browse mode"); an entirely empty criteria returns nothing.
/// </summary>
public sealed class SearchService : ISearchService
{
    /// <summary>Maximum results returned; nobody scrolls millions and this lets common queries stop scanning early.</summary>
    public const int MaxResults = 1000;

    private readonly IFileIndex _index;

    public SearchService(IFileIndex index) => _index = index;

    public Task<SearchResults> SearchAsync(SearchCriteria criteria, CancellationToken ct) => Task.Run(() =>
    {
        if (criteria.IsEmpty) return SearchResults.Empty;

        var snapshot = _index.AllNodes;
        var filter = NodeFilter.Create(criteria);
        var candidates = filter is null ? snapshot : snapshot.Where(filter.Matches);
        var matches = new List<FileNode>(capacity: 64);

        foreach (var node in UserSearchHelpers.FindAllMatches(criteria.Query, candidates, static n => n.Name))
        {
            ct.ThrowIfCancellationRequested();
            matches.Add(node);
            if (matches.Count >= MaxResults)
                return new SearchResults(matches, snapshot.Count, Capped: true);
        }

        return new SearchResults(matches, snapshot.Count, Capped: false);
    }, ct);

    /// <summary>
    /// Precompiled structured predicate, built once per search. Files and folders are two independent axes:
    /// folders pass only when <see cref="SearchCriteria.IncludeFolders"/> is set, while files pass the
    /// extension allowlist (null = every file, empty = no files, otherwise a specific set). Extension checks
    /// are allocation-free: <see cref="Path.GetExtension(ReadOnlySpan{char})"/> plus a span alternate lookup
    /// into the set, so the per-node cost stays trivial over millions of nodes and <see cref="FileNode"/>
    /// needs no extra fields. Size bounds are inclusive and apply to directories too — recursive directory
    /// size makes "folders over 1 GB" a useful query.
    /// </summary>
    private sealed class NodeFilter
    {
        private readonly bool _filesAllowed;                 // false => extension allowlist was empty ("no files")
        private readonly HashSet<string>? _extensions;       // null with _filesAllowed => every file passes
        private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _extensionLookup;
        private readonly bool _includeFolders;
        private readonly long? _minSize;
        private readonly long? _maxSize;
        private readonly DateTime? _afterUtc;
        private readonly DateTime? _beforeUtc;

        public static NodeFilter? Create(SearchCriteria c) => c.HasFilters ? new NodeFilter(c) : null;

        private NodeFilter(SearchCriteria c)
        {
            if (c.Extensions is null)
            {
                _filesAllowed = true;                        // no file-type restriction
            }
            else if (c.Extensions.Count == 0)
            {
                _filesAllowed = false;                       // every file excluded
            }
            else
            {
                _filesAllowed = true;
                _extensions = new HashSet<string>(c.Extensions, StringComparer.OrdinalIgnoreCase);
                _extensionLookup = _extensions.GetAlternateLookup<ReadOnlySpan<char>>();
            }

            _includeFolders = c.IncludeFolders;
            _minSize = c.MinSizeBytes;
            _maxSize = c.MaxSizeBytes;
            _afterUtc = c.ModifiedAfterUtc;
            _beforeUtc = c.ModifiedBeforeUtc;
        }

        public bool Matches(FileNode node)
        {
            if (node.IsDirectory)
            {
                if (!_includeFolders) return false;
            }
            else
            {
                if (!_filesAllowed) return false;
                if (_extensions is not null)
                {
                    var ext = Path.GetExtension(node.Name.AsSpan());
                    if (ext.IsEmpty || !_extensionLookup.Contains(ext)) return false;
                }
            }

            if (_minSize.HasValue && node.SizeBytes < _minSize.Value) return false;
            if (_maxSize.HasValue && node.SizeBytes > _maxSize.Value) return false;
            if (_afterUtc.HasValue && node.ModifiedUtc < _afterUtc.Value) return false;
            if (_beforeUtc.HasValue && node.ModifiedUtc >= _beforeUtc.Value) return false;

            return true;
        }
    }
}
