using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileCrawler.Models;
using FileCrawler.Services;
using FileCrawler.Utilities;

namespace FileCrawler.ViewModels;

/// <summary>One selectable extension (".png") inside a category's flyout.</summary>
public sealed partial class ExtensionOptionViewModel : ViewModelBase
{
    private readonly Action _changed;

    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    public ExtensionOptionViewModel(string name, Action changed)
    {
        Name = name;
        _changed = changed;
    }

    partial void OnIsSelectedChanged(bool value) => _changed();
}

/// <summary>
/// One file-type category row: a tri-state checkbox plus its per-extension children. The category state is
/// derived from the children (all → true, none → false, mixed → null); clicking the category checkbox fans
/// out to every child. The checkbox binds with IsThreeState="False" so user clicks only cycle true/false —
/// indeterminate is display-only, set from code.
/// </summary>
public sealed partial class FileCategoryViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _updating;

    public string Name { get; }
    public IReadOnlyList<ExtensionOptionViewModel> Extensions { get; }

    [ObservableProperty] private bool? _isChecked = false;

    public FileCategoryViewModel(FileCategory category, Action changed)
    {
        Name = category.Name;
        _changed = changed;
        Extensions = category.Extensions.Select(e => new ExtensionOptionViewModel(e, OnChildChanged)).ToList();
    }

    public IEnumerable<string> SelectedExtensions => Extensions.Where(e => e.IsSelected).Select(e => e.Name);

    /// <summary>Checks or unchecks every extension in the category without raising the change callback,
    /// so callers can batch bulk updates (Select all / Select none / Clear) into a single search rerun.</summary>
    public void SetAll(bool on)
    {
        _updating = true;
        foreach (var ext in Extensions) ext.IsSelected = on;
        IsChecked = on;
        _updating = false;
    }

    /// <summary>Selects exactly the extensions in <paramref name="selected"/> and recomputes the tri-state,
    /// without raising the change callback (for restoring a persisted selection).</summary>
    public void ApplySelection(IReadOnlySet<string> selected)
    {
        _updating = true;
        foreach (var ext in Extensions) ext.IsSelected = selected.Contains(ext.Name);
        var count = Extensions.Count(e => e.IsSelected);
        IsChecked = count == 0 ? false : count == Extensions.Count ? true : null;
        _updating = false;
    }

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_updating || value is not { } isOn) return;
        _updating = true;
        foreach (var ext in Extensions) ext.IsSelected = isOn;
        _updating = false;
        _changed();
    }

    private void OnChildChanged()
    {
        if (_updating) return;
        _updating = true;
        var selected = Extensions.Count(e => e.IsSelected);
        IsChecked = selected == 0 ? false : selected == Extensions.Count ? true : null;
        _updating = false;
        _changed();
    }
}

/// <summary>A date preset with a user-friendly label for the ComboBox.</summary>
public sealed record DatePresetOption(DatePreset Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// State for the filter bar: file-type categories, a Folders toggle, custom extensions, size bounds and a
/// modified-date window. The type checkboxes are an allowlist — everything starts checked (so a bare query
/// still shows all matches), and unchecking narrows results; unchecking every type matches nothing. Raises
/// <see cref="CriteriaChanged"/> on every change; the owner reruns the (debounced) search and calls
/// <see cref="BuildCriteria"/> at search time, so date presets are evaluated fresh each run. The selections
/// are persisted across restarts (see <see cref="CaptureState"/>/<see cref="RestoreState"/>) so the user's
/// last search comes back on launch.
/// </summary>
public sealed partial class SearchFiltersViewModel : ViewModelBase
{
    public static IReadOnlyList<SizeUnit> SizeUnits { get; } = [SizeUnit.B, SizeUnit.KB, SizeUnit.MB, SizeUnit.GB];

    public static IReadOnlyList<DatePresetOption> DatePresetOptions { get; } =
    [
        new(DatePreset.Any, "Any time"),
        new(DatePreset.Today, "Today"),
        new(DatePreset.Last7Days, "Last 7 days"),
        new(DatePreset.Last30Days, "Last 30 days"),
        new(DatePreset.ThisYear, "This year"),
        new(DatePreset.Custom, "Custom range…"),
    ];

    public IReadOnlyList<FileCategoryViewModel> Categories { get; }

    /// <summary>Folders excluded from this search only (via "Block subfolder → from this search"). Shown at the
    /// bottom of the filter bar, each removable with its own X; unlike a watched-folder block these never
    /// recrawl — they're applied at search time and cleared without touching the index.</summary>
    public ObservableCollection<BlockedFolderViewModel> SearchBlockedFolders { get; } = new();

    [ObservableProperty] private bool _includeFolders = true;
    [ObservableProperty] private string _customExtensionsText = "";
    [ObservableProperty] private string _minSizeText = "";
    [ObservableProperty] private string _maxSizeText = "";
    [ObservableProperty] private SizeUnit _minSizeUnit = SizeUnit.MB;
    [ObservableProperty] private SizeUnit _maxSizeUnit = SizeUnit.MB;
    [ObservableProperty] private DatePresetOption _selectedDatePreset = DatePresetOptions[0];
    [ObservableProperty] private DateTime? _customFromDate;
    [ObservableProperty] private DateTime? _customToDate;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private string _validationMessage = "";
    [ObservableProperty] private bool _hasActiveFilters;
    [ObservableProperty] private string _activeSummary = "";

    public bool IsCustomDate => SelectedDatePreset.Value == DatePreset.Custom;

    /// <summary>Suppresses per-change callbacks while a bulk update (Select all / none / Clear) runs,
    /// so the owner sees a single coalesced rerun rather than one per checkbox.</summary>
    private bool _suppressRaise;

    /// <summary>Raised whenever any filter changes; the owner should rerun its (debounced) search.</summary>
    public event Action? CriteriaChanged;

    public SearchFiltersViewModel()
    {
        Categories = FileCategories.All.Select(c => new FileCategoryViewModel(c, RaiseCriteriaChanged)).ToList();
        // Everything on by default: a plain query shows all matches, and the user narrows from there.
        foreach (var category in Categories) category.SetAll(true);
    }

    /// <summary>
    /// Excludes <paramref name="path"/> (and its contents) from this search only. Nested/duplicate paths are
    /// coalesced the same way watched-folder blocks are — a broader block supersedes the ones inside it — then
    /// reruns the search. Does not recrawl; the folder stays indexed and comes back when the block is removed.
    /// </summary>
    public void AddSearchBlock(string path)
    {
        var existing = SearchBlockedFolders.Select(b => b.Path).ToList();
        var resolution = WatchedFolderNesting.Resolve(path, existing);
        if (!resolution.CanAdd) return;

        foreach (var superseded in resolution.Superseded)
        {
            var vm = SearchBlockedFolders.FirstOrDefault(b => b.Path == superseded);
            if (vm is not null) SearchBlockedFolders.Remove(vm);
        }
        SearchBlockedFolders.Add(new BlockedFolderViewModel(path));
        RaiseCriteriaChanged();
    }

    /// <summary>Removes a per-search folder block (the X next to it) and reruns the search.</summary>
    [RelayCommand]
    private void RemoveSearchBlock(BlockedFolderViewModel? blocked)
    {
        if (blocked is null) return;
        if (SearchBlockedFolders.Remove(blocked)) RaiseCriteriaChanged();
    }

    /// <summary>Builds the criteria for one search run, refreshing validation and the active-filter summary.</summary>
    public SearchCriteria BuildCriteria(string query)
    {
        var messages = new List<string>();

        // Every category checked = "all files" (null, includes uncategorized extensions too). Otherwise the
        // checked categories plus any custom extensions form an allowlist — which may be empty, meaning no
        // files match at all.
        IReadOnlyCollection<string>? extensions;
        if (Categories.All(c => c.IsChecked == true))
        {
            extensions = null;
        }
        else
        {
            var selected = Categories.SelectMany(c => c.SelectedExtensions).ToList();
            foreach (var ext in FilterInputParser.ParseExtensions(CustomExtensionsText))
                if (!selected.Contains(ext)) selected.Add(ext);
            extensions = selected;
        }

        long? min = null, max = null;
        if (FilterInputParser.TryParseSize(MinSizeText, MinSizeUnit, out var minBytes, out var minInvalid)) min = minBytes;
        else if (minInvalid) messages.Add("Min size isn't a number — ignored.");
        if (FilterInputParser.TryParseSize(MaxSizeText, MaxSizeUnit, out var maxBytes, out var maxInvalid)) max = maxBytes;
        else if (maxInvalid) messages.Add("Max size isn't a number — ignored.");
        if (min.HasValue && max.HasValue && min > max) messages.Add("Min size is larger than max size.");

        var (afterUtc, beforeUtc) = DatePresets.ToUtcRange(
            SelectedDatePreset.Value, CustomFromDate, CustomToDate, DateTime.Now);

        ValidationMessage = string.Join(" ", messages);

        var blockedPaths = SearchBlockedFolders.Count == 0
            ? null
            : SearchBlockedFolders.Select(b => b.Path).ToList();

        var criteria = new SearchCriteria(
            query,
            extensions,
            min, max, afterUtc, beforeUtc,
            IncludeFolders,
            blockedPaths);

        UpdateSummary(criteria);
        return criteria;
    }

    /// <summary>Snapshots the current filter selections for persistence.</summary>
    public FilterState CaptureState() => new(
        Categories.SelectMany(c => c.SelectedExtensions).ToList(),
        IncludeFolders,
        CustomExtensionsText,
        MinSizeText,
        MaxSizeText,
        MinSizeUnit,
        MaxSizeUnit,
        SelectedDatePreset.Value,
        CustomFromDate,
        CustomToDate,
        SearchBlockedFolders.Select(b => b.Path).ToList());

    /// <summary>Restores previously persisted filter selections without triggering a search — the owner
    /// runs one search itself once restoration (and any folder crawl) is complete.</summary>
    public void RestoreState(FilterState state)
    {
        _suppressRaise = true;
        var selected = state.SelectedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var category in Categories) category.ApplySelection(selected);
        IncludeFolders = state.IncludeFolders;
        CustomExtensionsText = state.CustomExtensionsText;
        MinSizeText = state.MinSizeText;
        MaxSizeText = state.MaxSizeText;
        MinSizeUnit = state.MinSizeUnit;
        MaxSizeUnit = state.MaxSizeUnit;
        SelectedDatePreset =
            DatePresetOptions.FirstOrDefault(o => o.Value == state.DatePreset) ?? DatePresetOptions[0];
        CustomFromDate = state.CustomFromDate;
        CustomToDate = state.CustomToDate;
        SearchBlockedFolders.Clear();
        if (state.BlockedPaths is not null)
            foreach (var path in state.BlockedPaths) SearchBlockedFolders.Add(new BlockedFolderViewModel(path));
        _suppressRaise = false;
    }

    /// <summary>Checks every file type and folders — the non-restrictive default.</summary>
    [RelayCommand]
    private void SelectAll() => SetAllTypes(true);

    /// <summary>Unchecks every file type and folders, so nothing matches until the user picks a type.</summary>
    [RelayCommand]
    private void SelectNone() => SetAllTypes(false);

    private void SetAllTypes(bool on)
    {
        _suppressRaise = true;
        foreach (var category in Categories) category.SetAll(on);
        IncludeFolders = on;
        _suppressRaise = false;
        RaiseCriteriaChanged();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressRaise = true;
        foreach (var category in Categories) category.SetAll(true);
        IncludeFolders = true;
        CustomExtensionsText = "";
        MinSizeText = "";
        MaxSizeText = "";
        SelectedDatePreset = DatePresetOptions[0];
        CustomFromDate = null;
        CustomToDate = null;
        SearchBlockedFolders.Clear();
        ValidationMessage = "";
        _suppressRaise = false;
        // Everything above was suppressed; fire one change so the owner reruns the (debounced) search.
        RaiseCriteriaChanged();
    }

    private void UpdateSummary(SearchCriteria criteria)
    {
        var active = 0;
        if (criteria.Extensions is not null || !criteria.IncludeFolders) active++; // type allowlist / folders
        if (criteria.MinSizeBytes.HasValue || criteria.MaxSizeBytes.HasValue) active++;
        if (criteria.ModifiedAfterUtc.HasValue || criteria.ModifiedBeforeUtc.HasValue) active++;
        if (criteria.BlockedPaths is { Count: > 0 }) active++; // per-search folder blocks

        HasActiveFilters = active > 0;
        ActiveSummary = active == 0 ? "" : $"{active} filter(s) active";
    }

    private void RaiseCriteriaChanged()
    {
        if (_suppressRaise) return;
        CriteriaChanged?.Invoke();
    }

    partial void OnIncludeFoldersChanged(bool value) => RaiseCriteriaChanged();
    partial void OnCustomExtensionsTextChanged(string value) => RaiseCriteriaChanged();
    partial void OnMinSizeTextChanged(string value) => RaiseCriteriaChanged();
    partial void OnMaxSizeTextChanged(string value) => RaiseCriteriaChanged();
    partial void OnMinSizeUnitChanged(SizeUnit value) => RaiseCriteriaChanged();
    partial void OnMaxSizeUnitChanged(SizeUnit value) => RaiseCriteriaChanged();
    partial void OnCustomFromDateChanged(DateTime? value) => RaiseCriteriaChanged();
    partial void OnCustomToDateChanged(DateTime? value) => RaiseCriteriaChanged();

    partial void OnSelectedDatePresetChanged(DatePresetOption value)
    {
        OnPropertyChanged(nameof(IsCustomDate));
        RaiseCriteriaChanged();
    }
}
