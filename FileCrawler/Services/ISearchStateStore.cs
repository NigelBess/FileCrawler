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
    IReadOnlyList<string>? BlockedPaths = null,
    string SortColumn = "Name",
    bool SortDescending = false)
{
    /// <summary>
    /// Value equality that also compares the list members (which the compiler-generated record equality would
    /// compare by reference). Extension and blocked-path lists are compared as case-insensitive sets — order and
    /// a null-vs-empty distinction don't matter. Used to tell whether the filters still match a loaded preset.
    /// </summary>
    public bool IsEquivalentTo(FilterState other) =>
        IncludeFolders == other.IncludeFolders
        && CustomExtensionsText == other.CustomExtensionsText
        && MinSizeText == other.MinSizeText
        && MaxSizeText == other.MaxSizeText
        && MinSizeUnit == other.MinSizeUnit
        && MaxSizeUnit == other.MaxSizeUnit
        && DatePreset == other.DatePreset
        && CustomFromDate == other.CustomFromDate
        && CustomToDate == other.CustomToDate
        && SortColumn == other.SortColumn
        && SortDescending == other.SortDescending
        && SameSet(SelectedExtensions, other.SelectedExtensions)
        && SameSet(BlockedPaths, other.BlockedPaths);

    private static bool SameSet(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        var setA = new HashSet<string>(a ?? [], StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b ?? [], StringComparer.OrdinalIgnoreCase);
        return setA.SetEquals(setB);
    }
}

/// <summary>The last search term and filter selections, restored on the next launch.</summary>
public sealed record SearchState(string SearchText, FilterState Filters);

/// <summary>Persists the most recent search term and filters across runs.</summary>
public interface ISearchStateStore
{
    /// <summary>Returns the last persisted search state, or null if none has ever been saved.</summary>
    Task<SearchState?> LoadAsync();
    Task SaveAsync(SearchState state);
}
