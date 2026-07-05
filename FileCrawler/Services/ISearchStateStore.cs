using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FileCrawler.Utilities;

namespace FileCrawler.Services;

/// <summary>A persisted snapshot of the filter bar's state (everything the user can tweak in it).</summary>
public sealed record FilterState(
    IReadOnlyList<string> SelectedExtensions,
    bool IncludeFolders,
    string CustomExtensionsText,
    string MinSizeText,
    string MaxSizeText,
    SizeUnit MinSizeUnit,
    SizeUnit MaxSizeUnit,
    DatePreset DatePreset,
    DateTime? CustomFromDate,
    DateTime? CustomToDate,
    IReadOnlyList<string>? BlockedPaths = null);

/// <summary>The last search term and filter selections, restored on the next launch.</summary>
public sealed record SearchState(string SearchText, FilterState Filters);

/// <summary>Persists the most recent search term and filters across runs.</summary>
public interface ISearchStateStore
{
    /// <summary>Returns the last persisted search state, or null if none has ever been saved.</summary>
    Task<SearchState?> LoadAsync();
    Task SaveAsync(SearchState state);
}
