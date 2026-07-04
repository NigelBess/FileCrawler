using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileCrawler.Models;
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

    public void Reset()
    {
        _updating = true;
        foreach (var ext in Extensions) ext.IsSelected = false;
        IsChecked = false;
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

/// <summary>A files/folders choice with a user-friendly label for the ComboBox.</summary>
public sealed record KindOption(NodeKindFilter Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// State for the filter bar: file-type categories, custom extensions, size bounds, modified-date window and
/// files/folders kind. Raises <see cref="CriteriaChanged"/> on every change; the owner reruns the (debounced)
/// search and calls <see cref="BuildCriteria"/> at search time, so date presets are evaluated fresh each run.
/// Filters are deliberately per-session — persisting them across restarts risks invisible sticky filters.
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

    public static IReadOnlyList<KindOption> KindOptions { get; } =
    [
        new(NodeKindFilter.All, "Files & folders"),
        new(NodeKindFilter.FilesOnly, "Files only"),
        new(NodeKindFilter.FoldersOnly, "Folders only"),
    ];

    public IReadOnlyList<FileCategoryViewModel> Categories { get; }

    [ObservableProperty] private string _customExtensionsText = "";
    [ObservableProperty] private string _minSizeText = "";
    [ObservableProperty] private string _maxSizeText = "";
    [ObservableProperty] private SizeUnit _minSizeUnit = SizeUnit.MB;
    [ObservableProperty] private SizeUnit _maxSizeUnit = SizeUnit.MB;
    [ObservableProperty] private DatePresetOption _selectedDatePreset = DatePresetOptions[0];
    [ObservableProperty] private DateTime? _customFromDate;
    [ObservableProperty] private DateTime? _customToDate;
    [ObservableProperty] private KindOption _selectedKind = KindOptions[0];
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _validationMessage = "";
    [ObservableProperty] private bool _hasActiveFilters;
    [ObservableProperty] private string _activeSummary = "";

    public bool IsCustomDate => SelectedDatePreset.Value == DatePreset.Custom;

    /// <summary>Raised whenever any filter changes; the owner should rerun its (debounced) search.</summary>
    public event Action? CriteriaChanged;

    public SearchFiltersViewModel()
    {
        Categories = FileCategories.All.Select(c => new FileCategoryViewModel(c, RaiseCriteriaChanged)).ToList();
    }

    /// <summary>Builds the criteria for one search run, refreshing validation and the active-filter summary.</summary>
    public SearchCriteria BuildCriteria(string query)
    {
        var messages = new List<string>();

        var extensions = Categories.SelectMany(c => c.SelectedExtensions).ToList();
        foreach (var ext in FilterInputParser.ParseExtensions(CustomExtensionsText))
            if (!extensions.Contains(ext)) extensions.Add(ext);

        long? min = null, max = null;
        if (FilterInputParser.TryParseSize(MinSizeText, MinSizeUnit, out var minBytes, out var minInvalid)) min = minBytes;
        else if (minInvalid) messages.Add("Min size isn't a number — ignored.");
        if (FilterInputParser.TryParseSize(MaxSizeText, MaxSizeUnit, out var maxBytes, out var maxInvalid)) max = maxBytes;
        else if (maxInvalid) messages.Add("Max size isn't a number — ignored.");
        if (min.HasValue && max.HasValue && min > max) messages.Add("Min size is larger than max size.");

        var (afterUtc, beforeUtc) = DatePresets.ToUtcRange(
            SelectedDatePreset.Value, CustomFromDate, CustomToDate, DateTime.Now);

        ValidationMessage = string.Join(" ", messages);

        var criteria = new SearchCriteria(
            query,
            extensions.Count > 0 ? extensions : null,
            min, max, afterUtc, beforeUtc,
            SelectedKind.Value);

        UpdateSummary(criteria);
        return criteria;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var category in Categories) category.Reset();
        CustomExtensionsText = "";
        MinSizeText = "";
        MaxSizeText = "";
        SelectedDatePreset = DatePresetOptions[0];
        CustomFromDate = null;
        CustomToDate = null;
        SelectedKind = KindOptions[0];
        ValidationMessage = "";
        // Category resets suppress their callbacks and properties already at defaults raise nothing,
        // so fire one explicit change; any raises above coalesce into it via the owner's debounce.
        RaiseCriteriaChanged();
    }

    private void UpdateSummary(SearchCriteria criteria)
    {
        var active = 0;
        if (criteria.Extensions is { Count: > 0 }) active++;
        if (criteria.MinSizeBytes.HasValue || criteria.MaxSizeBytes.HasValue) active++;
        if (criteria.ModifiedAfterUtc.HasValue || criteria.ModifiedBeforeUtc.HasValue) active++;
        if (criteria.Kind != NodeKindFilter.All) active++;

        HasActiveFilters = active > 0;
        ActiveSummary = active == 0 ? "" : $"{active} filter(s) active";
    }

    private void RaiseCriteriaChanged() => CriteriaChanged?.Invoke();

    partial void OnCustomExtensionsTextChanged(string value) => RaiseCriteriaChanged();
    partial void OnMinSizeTextChanged(string value) => RaiseCriteriaChanged();
    partial void OnMaxSizeTextChanged(string value) => RaiseCriteriaChanged();
    partial void OnMinSizeUnitChanged(SizeUnit value) => RaiseCriteriaChanged();
    partial void OnMaxSizeUnitChanged(SizeUnit value) => RaiseCriteriaChanged();
    partial void OnCustomFromDateChanged(DateTime? value) => RaiseCriteriaChanged();
    partial void OnCustomToDateChanged(DateTime? value) => RaiseCriteriaChanged();
    partial void OnSelectedKindChanged(KindOption value) => RaiseCriteriaChanged();

    partial void OnSelectedDatePresetChanged(DatePresetOption value)
    {
        OnPropertyChanged(nameof(IsCustomDate));
        RaiseCriteriaChanged();
    }
}
