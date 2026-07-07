using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCrawler.Services;
using FileCrawler.Utilities;
using FileCrawler.ViewModels;
using FileCrawler.Views;
using Xunit;

namespace FileCrawler.Tests;

/// <summary>In-memory preset store so tests don't touch %LOCALAPPDATA%.</summary>
internal sealed class InMemoryPresetStore : IFilterPresetStore
{
    private List<FilterPreset> _presets = new();
    public IReadOnlyList<FilterPreset> Saved => _presets;
    public void Seed(params FilterPreset[] presets) => _presets = presets.ToList();
    public IReadOnlyList<FilterPreset> Load() => _presets.ToList();
    public void Save(IReadOnlyList<FilterPreset> presets) => _presets = presets.ToList();
}

/// <summary>A save prompt that returns a fixed, pre-scripted answer (no dialog) for tests.</summary>
internal sealed class FakeSavePrompt : IPresetSavePrompt
{
    private readonly SavePresetResult? _result;
    public string? LastCurrentName { get; private set; }
    public FakeSavePrompt(SavePresetResult? result) => _result = result;
    public Task<SavePresetResult?> PromptAsync(string? currentPresetName)
    {
        LastCurrentName = currentPresetName;
        return Task.FromResult(_result);
    }
}

public class FilterPresetTests
{
    private static FilterState SampleState(
        string extension = ".pdf", string sortColumn = "Name", bool sortDescending = false) => new(
        SelectedExtensions: new[] { extension },
        IncludeFolders: true, CustomExtensionsText: "", MinSizeText: "", MaxSizeText: "",
        MinSizeUnit: SizeUnit.MB, MaxSizeUnit: SizeUnit.MB, DatePreset: DatePreset.Any,
        CustomFromDate: null, CustomToDate: null, BlockedPaths: null,
        SortColumn: sortColumn, SortDescending: sortDescending);

    private static FilterPresetsViewModel Build(
        IFilterPresetStore store, IPresetSavePrompt prompt, out SearchFiltersViewModel filters,
        IConfirmationService? confirm = null)
    {
        filters = new SearchFiltersViewModel();
        return new FilterPresetsViewModel(
            filters, store, confirm ?? new FakeConfirmationService(true), prompt, () => { });
    }

    [Fact]
    public void Saving_with_no_loaded_preset_creates_and_persists_a_new_preset()
    {
        var store = new InMemoryPresetStore();
        var prompt = new FakeSavePrompt(new SavePresetResult(SavePresetAction.CreateNew, "Recents"));
        var vm = Build(store, prompt, out _);

        vm.SaveCommand.Execute(null);

        Assert.Null(prompt.LastCurrentName); // no preset was loaded
        var preset = Assert.Single(vm.Presets);
        Assert.Equal("Recents", preset.Name);
        Assert.True(preset.IsSelected);
        Assert.Equal("Recents", vm.CurrentTitle);
        Assert.Single(store.Saved);
        Assert.Equal("Recents", store.Saved[0].Name);
    }

    [Fact]
    public void Applying_a_preset_restores_its_filters_and_sort_selects_it_and_reruns_the_search()
    {
        var store = new InMemoryPresetStore();
        store.Seed(new FilterPreset("Docs", SampleState(sortColumn: "Size", sortDescending: true)));
        var filters = new SearchFiltersViewModel();
        var reruns = 0;
        var vm = new FilterPresetsViewModel(
            filters, store, new FakeConfirmationService(true), new FakeSavePrompt(null), () => reruns++);

        var preset = Assert.Single(vm.Presets);
        preset.ApplyCommand.Execute(null);

        Assert.Same(preset, vm.SelectedPreset);
        Assert.True(preset.IsSelected);
        Assert.False(vm.IsModified);
        Assert.Equal("Docs", vm.CurrentTitle);
        Assert.Equal(1, reruns);
        // The restored allowlist is exactly ".pdf".
        Assert.Equal(new[] { ".pdf" }, filters.Categories.SelectMany(c => c.SelectedExtensions).ToArray());
        // The preset's saved sort order was restored onto the filters too.
        Assert.Equal(ResultSortColumn.Size, filters.SortColumn);
        Assert.True(filters.SortDescending);
    }

    [Fact]
    public void Editing_filters_after_applying_a_preset_marks_it_modified_and_stars_the_title()
    {
        var store = new InMemoryPresetStore();
        store.Seed(new FilterPreset("Docs", SampleState()));
        var vm = Build(store, new FakeSavePrompt(null), out var filters);

        vm.Presets[0].ApplyCommand.Execute(null);
        Assert.False(vm.IsModified);

        // Change a filter — now it diverges from the loaded preset.
        filters.MinSizeText = "5";

        Assert.True(vm.IsModified);
        Assert.Equal("Docs*", vm.CurrentTitle);
    }

    [Fact]
    public void Changing_the_sort_order_after_applying_a_preset_marks_it_modified()
    {
        var store = new InMemoryPresetStore();
        store.Seed(new FilterPreset("Docs", SampleState(sortColumn: "Name", sortDescending: false)));
        var vm = Build(store, new FakeSavePrompt(null), out var filters);

        vm.Presets[0].ApplyCommand.Execute(null);
        Assert.False(vm.IsModified);

        // Re-sort by a different column — sort is part of the filter, so the preset is now modified.
        filters.ToggleSort(ResultSortColumn.Size);

        Assert.True(vm.IsModified);
        Assert.Equal("Docs*", vm.CurrentTitle);
    }

    [Fact]
    public void Saving_a_new_preset_captures_the_current_sort_order()
    {
        var store = new InMemoryPresetStore();
        var prompt = new FakeSavePrompt(new SavePresetResult(SavePresetAction.CreateNew, "Big first"));
        var vm = Build(store, prompt, out var filters);

        filters.ToggleSort(ResultSortColumn.Size); // Size, ascending
        filters.ToggleSort(ResultSortColumn.Size); // toggles to descending

        vm.SaveCommand.Execute(null);

        Assert.Equal("Size", store.Saved[0].State.SortColumn);
        Assert.True(store.Saved[0].State.SortDescending);
    }

    [Fact]
    public void Updating_the_loaded_preset_overwrites_it_in_place_and_clears_the_modified_flag()
    {
        var store = new InMemoryPresetStore();
        store.Seed(new FilterPreset("Docs", SampleState()));
        var prompt = new FakeSavePrompt(new SavePresetResult(SavePresetAction.UpdateExisting, "Docs"));
        var vm = Build(store, prompt, out var filters);

        vm.Presets[0].ApplyCommand.Execute(null);
        filters.MinSizeText = "5";
        Assert.True(vm.IsModified);

        vm.SaveCommand.Execute(null);

        Assert.Equal("Docs", prompt.LastCurrentName); // dialog was told which preset is loaded
        Assert.Single(vm.Presets);                    // updated in place, not duplicated
        Assert.False(vm.IsModified);
        Assert.Equal("Docs", vm.CurrentTitle);
        Assert.Equal("5", store.Saved[0].State.MinSizeText);
    }

    [Fact]
    public void Deleting_a_preset_removes_it_and_clears_the_selection_when_it_was_loaded()
    {
        var store = new InMemoryPresetStore();
        store.Seed(new FilterPreset("Docs", SampleState()));
        var vm = Build(store, new FakeSavePrompt(null), out _);

        vm.Presets[0].ApplyCommand.Execute(null);
        Assert.NotNull(vm.SelectedPreset);

        vm.Presets[0].DeleteCommand.Execute(null);

        Assert.Empty(vm.Presets);
        Assert.Null(vm.SelectedPreset);
        Assert.Equal("", vm.CurrentTitle);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void A_declined_delete_confirmation_keeps_the_preset()
    {
        var store = new InMemoryPresetStore();
        store.Seed(new FilterPreset("Docs", SampleState()));
        var vm = Build(store, new FakeSavePrompt(null), out _, new FakeConfirmationService(false));

        vm.Presets[0].DeleteCommand.Execute(null);

        Assert.Single(vm.Presets);
    }

    [AvaloniaFact]
    public void Save_dialog_loads_and_offers_the_update_choice_when_a_preset_is_loaded()
    {
        // Constructing the dialog runs its XAML (catches typos like a bad icon Kind) and configures the choice.
        var dialog = new SavePresetDialog("Recents");

        var choice = dialog.FindControl<StackPanel>("ChoicePanel");
        var update = dialog.FindControl<RadioButton>("UpdateOption");
        Assert.NotNull(choice);
        Assert.True(choice!.IsVisible);
        Assert.Equal("Update “Recents”", update!.Content);
    }

    [AvaloniaFact]
    public void Save_dialog_hides_the_choice_when_no_preset_is_loaded()
    {
        var dialog = new SavePresetDialog(null);
        var choice = dialog.FindControl<StackPanel>("ChoicePanel");
        Assert.NotNull(choice);
        Assert.False(choice!.IsVisible);
    }
}
