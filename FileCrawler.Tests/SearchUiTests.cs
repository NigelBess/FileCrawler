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
            new SearchService(index), new FakeFolderPicker(_tree));
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

    /// <summary>No-op persistence so tests don't touch %LOCALAPPDATA%.</summary>
    private sealed class NoopStore : IWatchedFolderStore
    {
        public Task<System.Collections.Generic.IReadOnlyList<string>> LoadAsync() =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(Array.Empty<string>());
        public Task SaveAsync(System.Collections.Generic.IEnumerable<string> folders) => Task.CompletedTask;
    }
}
