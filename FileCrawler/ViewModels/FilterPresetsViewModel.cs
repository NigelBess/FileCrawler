using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileCrawler.Services;

namespace FileCrawler.ViewModels;

/// <summary>One saved filter preset, shown as a button in the presets row.</summary>
public sealed partial class FilterPresetViewModel : ViewModelBase
{
    private readonly Func<FilterPresetViewModel, Task> _apply;
    private readonly Func<FilterPresetViewModel, Task> _delete;

    public string Name { get; }
    public FilterState State { get; }

    /// <summary>True while this is the preset the filters were last loaded from (highlights its button).</summary>
    [ObservableProperty] private bool _isSelected;

    public FilterPresetViewModel(
        FilterPreset preset, Func<FilterPresetViewModel, Task> apply, Func<FilterPresetViewModel, Task> delete)
    {
        Name = preset.Name;
        State = preset.State;
        _apply = apply;
        _delete = delete;
    }

    public FilterPreset ToModel() => new(Name, State);

    /// <summary>Loads this preset's filters (the button click).</summary>
    [RelayCommand]
    private Task Apply() => _apply(this);

    /// <summary>Deletes this preset (the right-click context-menu item).</summary>
    [RelayCommand]
    private Task Delete() => _delete(this);
}

/// <summary>
/// Manages the row of saved filter presets shown above the filter bar. Applying a preset restores its filters
/// (and reruns the search); saving snapshots the current filters as a new preset or updates the loaded one.
/// Tracks whether the loaded preset has since been modified so the title can show e.g. "Recents*".
/// </summary>
public sealed partial class FilterPresetsViewModel : ViewModelBase
{
    private readonly SearchFiltersViewModel _filters;
    private readonly IFilterPresetStore _store;
    private readonly IConfirmationService _confirm;
    private readonly IPresetSavePrompt _savePrompt;
    private readonly Action _rerunSearch;

    public ObservableCollection<FilterPresetViewModel> Presets { get; } = new();

    [ObservableProperty] private FilterPresetViewModel? _selectedPreset;
    [ObservableProperty] private bool _isModified;

    /// <summary>The title shown above the filters: the loaded preset's name, with a trailing "*" once its filters
    /// have been modified. Empty when no preset is loaded.</summary>
    public string CurrentTitle =>
        SelectedPreset is null ? "" : IsModified ? SelectedPreset.Name + "*" : SelectedPreset.Name;

    public bool HasSelectedPreset => SelectedPreset is not null;

    public FilterPresetsViewModel(
        SearchFiltersViewModel filters, IFilterPresetStore store, IConfirmationService confirm,
        IPresetSavePrompt savePrompt, Action rerunSearch)
    {
        _filters = filters;
        _store = store;
        _confirm = confirm;
        _savePrompt = savePrompt;
        _rerunSearch = rerunSearch;

        foreach (var preset in _store.Load()) Presets.Add(Wrap(preset));

        // Any filter tweak after loading a preset marks it modified (drives the "*" in the title). Filter edits
        // raise CriteriaChanged; sort edits don't (they re-sort in place, not re-search) so they come through as
        // property changes — both are captured in CaptureState, so either path just recomputes the diff.
        _filters.CriteriaChanged += RecomputeModified;
        _filters.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SearchFiltersViewModel.SortColumn)
                or nameof(SearchFiltersViewModel.SortDescending))
                RecomputeModified();
        };
    }

    private FilterPresetViewModel Wrap(FilterPreset preset) => new(preset, ApplyPresetAsync, DeletePresetAsync);

    private Task ApplyPresetAsync(FilterPresetViewModel preset)
    {
        // RestoreState suppresses CriteriaChanged, but its sort assignment still fires a property change that
        // recomputes IsModified — so set the selection first, then clear the flag: the load is clean by definition.
        _filters.RestoreState(preset.State);
        SelectedPreset = preset;
        IsModified = false;
        _rerunSearch();
        return Task.CompletedTask;
    }

    private async Task DeletePresetAsync(FilterPresetViewModel preset)
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Delete preset?", $"Delete the filter preset “{preset.Name}”? This can't be undone.", "Delete");
        if (!confirmed) return;

        Presets.Remove(preset);
        if (SelectedPreset == preset)
        {
            SelectedPreset = null;
            IsModified = false;
        }
        Persist();
    }

    /// <summary>Saves the current filters — creating a new preset or updating the loaded one (prompts the user).</summary>
    [RelayCommand]
    private async Task Save()
    {
        var result = await _savePrompt.PromptAsync(SelectedPreset?.Name);
        if (result is null) return;

        var state = _filters.CaptureState();

        if (result.Action == SavePresetAction.UpdateExisting && SelectedPreset is not null)
        {
            var updated = Wrap(new FilterPreset(SelectedPreset.Name, state));
            Presets[Presets.IndexOf(SelectedPreset)] = updated;
            SelectedPreset = updated;
        }
        else
        {
            // Creating a new preset; a name that collides with an existing one overwrites that preset in place.
            var created = Wrap(new FilterPreset(result.Name, state));
            var existing = Presets.FirstOrDefault(
                p => string.Equals(p.Name, result.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) Presets[Presets.IndexOf(existing)] = created;
            else Presets.Add(created);
            SelectedPreset = created;
        }

        IsModified = false;
        Persist();
    }

    private void RecomputeModified()
    {
        if (SelectedPreset is null) return;
        IsModified = !_filters.CaptureState().IsEquivalentTo(SelectedPreset.State);
    }

    private void Persist() => _store.Save(Presets.Select(p => p.ToModel()).ToList());

    partial void OnSelectedPresetChanged(FilterPresetViewModel? oldValue, FilterPresetViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(HasSelectedPreset));
    }

    partial void OnIsModifiedChanged(bool value) => OnPropertyChanged(nameof(CurrentTitle));
}
