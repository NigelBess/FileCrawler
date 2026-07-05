using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCrawler.Services;
using FileCrawler.ViewModels;
using FileCrawler.Views;
using Xunit;

namespace FileCrawler.Tests;

/// <summary>A folder picker that returns a preset path (no dialog) for tests.</summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    private readonly string _path;
    public FakeFolderPicker(string path) => _path = path;
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(_path);
}

/// <summary>A block picker that auto-selects the deepest offered level (no dialog) for tests.</summary>
internal sealed class FakeSubfolderBlockPicker : ISubfolderBlockPicker
{
    public Task<string?> PickAsync(System.Collections.Generic.IReadOnlyList<string> candidatePaths) =>
        Task.FromResult<string?>(candidatePaths.Count > 0 ? candidatePaths[^1] : null);
}

public class SearchUiTests : IDisposable
{
    private readonly string _tree;

    public SearchUiTests()
    {
        _tree = Path.Combine(Path.GetTempPath(), "FileCrawlerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tree, "Projects", "2026"));
        Directory.CreateDirectory(Path.Combine(_tree, "Get Packed Bags"));
        File.WriteAllText(Path.Combine(_tree, "Projects", "2026", "report.pdf"), "hello");
        File.WriteAllText(Path.Combine(_tree, "Get Packed Bags", "list.txt"), "yy");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tree, recursive: true); } catch { /* best effort */ }
    }

    private (MainWindow window, MainWindowViewModel vm) BuildUi()
    {
        var index = new FileIndex();
        var vm = new MainWindowViewModel(
            new DirectoryCrawler(), index, new NoopStore(),
            new SearchService(index), new FakeFolderPicker(_tree), new FakeSubfolderBlockPicker());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public async Task AddFolder_then_search_populates_the_results_list()
    {
        var (window, vm) = BuildUi();

        // Add a watched folder through the real command (uses the fake picker).
        await vm.AddFolderCommand.ExecuteAsync(null);
        Assert.Single(vm.WatchedFolders);

        // Type a query and let the debounce + background search complete.
        vm.SearchText = "report";
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        // Assert against the actual ListBox in the visual tree, not just the VM.
        var list = window.FindControl<ListBox>("ResultsList");
        Assert.NotNull(list);
        var items = list!.ItemsSource!.Cast<SearchResultViewModel>().ToList();
        Assert.Contains(items, r => r.Name == "report.pdf");
    }

    [AvaloniaFact]
    public async Task Forgiving_prefix_match_finds_multiword_folder()
    {
        var (window, vm) = BuildUi();
        await vm.AddFolderCommand.ExecuteAsync(null);

        vm.SearchText = "gepaba"; // Get Packed Bags
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(vm.Results, r => r.Name == "Get Packed Bags");
    }

    [AvaloniaFact]
    public async Task Empty_query_shows_no_results()
    {
        var (_, vm) = BuildUi();
        await vm.AddFolderCommand.ExecuteAsync(null);

        vm.SearchText = "";
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Results);
    }

    [AvaloniaFact]
    public async Task Blocking_a_subfolder_excludes_its_contents_and_unblocking_restores_them()
    {
        var (_, vm) = BuildUi();
        await vm.AddFolderCommand.ExecuteAsync(null);

        vm.SearchText = "report";
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
        var result = vm.Results.Single(r => r.Name == "report.pdf");

        // Block the subfolder the file lives in (…/Projects/2026); its contents should disappear.
        await vm.BlockSubfolderCommand.ExecuteAsync(result);
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        // The block is nested under the watched folder that owns it.
        var folder = vm.WatchedFolders.Single();
        Assert.Contains(folder.BlockedSubfolders, b => b.Path.EndsWith("2026"));
        Assert.DoesNotContain(vm.Results, r => r.Name == "report.pdf");

        // Unblocking recrawls and brings the file back.
        await vm.UnblockSubfolderCommand.ExecuteAsync(folder.BlockedSubfolders.Single());
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(folder.BlockedSubfolders);
        Assert.Contains(vm.Results, r => r.Name == "report.pdf");
    }

    [AvaloniaFact]
    public async Task Blocked_summary_shows_for_a_real_search_but_hides_when_filters_match_nothing()
    {
        var (_, vm) = BuildUi();
        await vm.AddFolderCommand.ExecuteAsync(null);

        // Block …/Projects/2026 (holds report.pdf) so the index has blocked content to report on.
        vm.SearchText = "report";
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
        await vm.BlockSubfolderCommand.ExecuteAsync(vm.Results.Single(r => r.Name == "report.pdf"));
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        // A query that matches a non-blocked item surfaces the blocked-coverage caveat.
        vm.SearchText = "list";
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.Results, r => r.Name == "list.txt");
        Assert.NotEqual("", vm.BlockedSummary);

        // "Select none" excludes every type and folders, so nothing can match — the caveat is pure noise here.
        vm.Filters.SelectNoneCommand.Execute(null);
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Results);
        Assert.Equal("", vm.BlockedSummary);
    }

    [AvaloniaFact]
    public async Task Extension_filter_narrows_results_and_clear_restores_them()
    {
        var (window, vm) = BuildUi();
        await vm.AddFolderCommand.ExecuteAsync(null);

        // "report list" would match nothing; search each file by a shared term instead.
        vm.SearchText = "t"; // matches report.pdf, list.txt, Get Packed Bags, Projects
        // Start from nothing selected, then narrow to just .txt files.
        vm.Filters.SelectNoneCommand.Execute(null);
        var txtOption = vm.Filters.Categories.Single(c => c.Name == "Documents")
            .Extensions.Single(e => e.Name == ".txt");
        txtOption.IsSelected = true;
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        var list = window.FindControl<ListBox>("ResultsList");
        var items = list!.ItemsSource!.Cast<SearchResultViewModel>().ToList();
        Assert.Contains(items, r => r.Name == "list.txt");
        Assert.DoesNotContain(items, r => r.Name == "report.pdf");

        vm.Filters.ClearFiltersCommand.Execute(null);
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        items = list.ItemsSource!.Cast<SearchResultViewModel>().ToList();
        Assert.Contains(items, r => r.Name == "report.pdf");
    }

    [AvaloniaFact]
    public async Task Filters_only_search_with_empty_query_shows_results()
    {
        var (_, vm) = BuildUi();
        await vm.AddFolderCommand.ExecuteAsync(null);

        vm.SearchText = "";
        // Browse just Documents: clear everything (also drops folders), then check Documents.
        vm.Filters.SelectNoneCommand.Execute(null);
        vm.Filters.Categories.Single(c => c.Name == "Documents").IsChecked = true;
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(vm.Results, r => r.Name == "report.pdf");
        Assert.Contains(vm.Results, r => r.Name == "list.txt");
        Assert.DoesNotContain(vm.Results, r => r.Name == "Get Packed Bags"); // folders unchecked
    }

    /// <summary>No-op persistence so tests don't touch %LOCALAPPDATA%.</summary>
    private sealed class NoopStore : IWatchedFolderStore
    {
        public Task<WatchedFolderState> LoadAsync() => Task.FromResult(WatchedFolderState.Empty);
        public Task SaveAsync(
            System.Collections.Generic.IEnumerable<string> folders,
            System.Collections.Generic.IEnumerable<string> blocked) => Task.CompletedTask;
    }
}
