using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;
using FileCrawler.Utilities;

namespace FileCrawler.Services;

/// <summary>
/// Searches the file index by node name, reusing <see cref="UserSearchHelpers.FindAllMatches"/> for the
/// forgiving match logic. The scan runs off the UI thread, is cancellable per item, and stops once the result
/// cap is reached. <see cref="UserSearchHelpers.FindAllMatches"/> returns a lazy enumerable, so capping the
/// consumer here is enough to keep it responsive over millions of nodes.
/// </summary>
public sealed class SearchService : ISearchService
{
    /// <summary>Maximum results returned; nobody scrolls millions and this lets common queries stop scanning early.</summary>
    public const int MaxResults = 1000;

    private readonly IFileIndex _index;

    public SearchService(IFileIndex index) => _index = index;

    public Task<SearchResults> SearchAsync(string query, CancellationToken ct) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(query)) return SearchResults.Empty;

        var snapshot = _index.AllNodes;
        var matches = new List<FileNode>(capacity: 64);

        foreach (var node in UserSearchHelpers.FindAllMatches(query, snapshot, static n => n.Name))
        {
            ct.ThrowIfCancellationRequested();
            matches.Add(node);
            if (matches.Count >= MaxResults)
                return new SearchResults(matches, snapshot.Count, Capped: true);
        }

        return new SearchResults(matches, snapshot.Count, Capped: false);
    }, ct);
}
