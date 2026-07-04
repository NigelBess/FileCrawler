using System.Linq;
using FileCrawler.Models;
using FileCrawler.Utilities;
using FileCrawler.ViewModels;
using Xunit;

namespace FileCrawler.Tests;

public class FileCategoryViewModelTests
{
    [Fact]
    public void Checking_the_category_selects_every_extension()
    {
        var changes = 0;
        var vm = new FileCategoryViewModel(new FileCategory("Images", [".png", ".jpg"]), () => changes++);

        vm.IsChecked = true;

        Assert.All(vm.Extensions, e => Assert.True(e.IsSelected));
        Assert.Equal(1, changes); // fan-out raises once, not per child
    }

    [Fact]
    public void Child_toggles_derive_the_tri_state_parent()
    {
        var vm = new FileCategoryViewModel(new FileCategory("Images", [".png", ".jpg"]), () => { });

        vm.Extensions[0].IsSelected = true;
        Assert.Null(vm.IsChecked); // mixed

        vm.Extensions[1].IsSelected = true;
        Assert.True(vm.IsChecked); // all

        vm.Extensions[0].IsSelected = false;
        vm.Extensions[1].IsSelected = false;
        Assert.False(vm.IsChecked); // none
    }

    [Fact]
    public void Parent_derivation_does_not_feed_back_into_children()
    {
        var changes = 0;
        var vm = new FileCategoryViewModel(new FileCategory("Images", [".png", ".jpg"]), () => changes++);

        vm.Extensions[0].IsSelected = true; // parent becomes indeterminate

        Assert.True(vm.Extensions[0].IsSelected);
        Assert.False(vm.Extensions[1].IsSelected); // untouched by the parent update
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SelectedExtensions_reflects_only_checked_children()
    {
        var vm = new FileCategoryViewModel(new FileCategory("Images", [".png", ".jpg", ".gif"]), () => { });

        vm.Extensions[1].IsSelected = true;

        Assert.Equal([".jpg"], vm.SelectedExtensions);
    }

    [Fact]
    public void ClearFilters_resets_everything_and_summary_flips()
    {
        var filters = new SearchFiltersViewModel();
        filters.Categories[0].IsChecked = true;
        filters.CustomExtensionsText = "psd";
        filters.MinSizeText = "1";
        filters.SelectedDatePreset = SearchFiltersViewModel.DatePresetOptions.Single(o => o.Value == DatePreset.Today);
        filters.SelectedKind = SearchFiltersViewModel.KindOptions.Single(o => o.Value == NodeKindFilter.FilesOnly);

        var criteria = filters.BuildCriteria("");
        Assert.True(criteria.HasFilters);
        Assert.True(filters.HasActiveFilters);
        Assert.Equal("4 filter(s) active", filters.ActiveSummary);

        filters.ClearFiltersCommand.Execute(null);

        var cleared = filters.BuildCriteria("");
        Assert.True(cleared.IsEmpty);
        Assert.False(filters.HasActiveFilters);
        Assert.Equal("", filters.ActiveSummary);
        Assert.All(filters.Categories, c => Assert.False(c.IsChecked));
    }

    [Fact]
    public void BuildCriteria_unions_category_and_custom_extensions_without_duplicates()
    {
        var filters = new SearchFiltersViewModel();
        var images = filters.Categories.Single(c => c.Name == "Images");
        images.IsChecked = true;
        filters.CustomExtensionsText = "png, psd"; // .png already in Images

        var criteria = filters.BuildCriteria("");

        Assert.NotNull(criteria.Extensions);
        Assert.Contains(".psd", criteria.Extensions!);
        Assert.Equal(images.Extensions.Count + 1, criteria.Extensions!.Count);
    }

    [Fact]
    public void Invalid_size_input_sets_validation_but_still_searches()
    {
        var filters = new SearchFiltersViewModel();
        filters.MinSizeText = "abc";

        var criteria = filters.BuildCriteria("query");

        Assert.Null(criteria.MinSizeBytes);
        Assert.Contains("Min size", filters.ValidationMessage);
    }
}
