using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileCrawler.Models;
using FileCrawler.Services;

namespace FileCrawler.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const int SearchDebounceMs = 200;

    private readonly IDirectoryCrawler _crawler;
    private readonly IFileIndex _index;
    private readonly IWatchedFolderStore _store;
    private readonly ISearchService _search;
    private readonly IFolderPicker _picker;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _resultsCts;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Add a folder to start searching.";
    [ObservableProperty] private string _resultsSummary = "";

    public ObservableCollection<WatchedFolderViewModel> WatchedFolders { get; } = new();
    public ObservableCollection<SearchResultViewModel> Results { get; } = new();
    public SearchFiltersViewModel Filters { get; } = new();

    /// <summary>Design-time constructor (also used by the XAML previewer).</summary>
    public MainWindowViewModel()
    {
        _crawler = new DirectoryCrawler();
        _index = new FileIndex();
        _store = new WatchedFolderStore();
        _search = new SearchService(_index);
        _picker = new StorageFolderPicker(() => null);
        Filters.CriteriaChanged += RerunSearch;
    }

    public MainWindowViewModel(
        IDirectoryCrawler crawler,
        IFileIndex index,
        IWatchedFolderStore store,
        ISearchService search,
        IFolderPicker picker)
    {
        _crawler = crawler;
        _index = index;
        _store = store;
        _search = search;
        _picker = picker;
        Filters.CriteriaChanged += RerunSearch;
    }

    /// <summary>Loads persisted watched folders and crawls them concurrently. Call once after the window shows.</summary>
    public async Task InitializeAsync()
    {
        var saved = await _store.LoadAsync();
        if (saved.Count == 0) return;

        IsBusy = true;
        StatusText = $"Crawling {saved.Count} folder(s)…";

        await Task.WhenAll(saved.Select(path => CrawlAndAddAsync(path)));

        IsBusy = false;
        StatusText = WatchedFolders.Count == 0
            ? "Add a folder to start searching."
            : $"Ready — {_index.AllNodes.Count:N0} items indexed across {WatchedFolders.Count} folder(s).";
    }

    // --- Search (debounced) ---

    partial void OnSearchTextChanged(string value) => _ = RunSearchAsync(value);

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        try
        {
            await Task.Delay(SearchDebounceMs, cts.Token);
            // Build criteria after the debounce so rapid filter clicks coalesce into one evaluation.
            var criteria = Filters.BuildCriteria(query);
            var results = await _search.SearchAsync(criteria, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            // Retire the displayed result set: any of its thumbnail decodes still queued exit early.
            _resultsCts?.Cancel();
            _resultsCts = new CancellationTokenSource();
            var lifetime = _resultsCts.Token;

            Results.Clear();
            foreach (var node in results.Items) Results.Add(new SearchResultViewModel(node, lifetime));

            ResultsSummary = criteria.IsEmpty
                ? ""
                : results.Capped
                    ? $"Showing first {results.Items.Count:N0} of many — refine your search."
                    : string.IsNullOrWhiteSpace(query)
                        ? $"{results.Items.Count:N0} filtered item(s)."
                        : $"{results.Items.Count:N0} result(s).";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke.
        }
    }

    // --- Commands ---

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var picked = await _picker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(picked)) return;

        var candidate = WatchedFolderNesting.Normalize(picked);
        var existing = WatchedFolders.Select(w => w.Path).ToList();
        var resolution = WatchedFolderNesting.Resolve(candidate, existing);

        if (!resolution.CanAdd)
        {
            StatusText = $"“{candidate}” is already covered by watched folder “{resolution.CoveredBy}”.";
            return;
        }

        // Remove any existing roots now nested inside the new one.
        foreach (var superseded in resolution.Superseded)
        {
            var vm = WatchedFolders.FirstOrDefault(w => w.Path == superseded);
            if (vm is not null)
            {
                _index.RemoveRoot(vm.Root);
                WatchedFolders.Remove(vm);
            }
        }

        IsBusy = true;
        await CrawlAndAddAsync(candidate);
        await PersistAsync();
        IsBusy = false;

        StatusText = resolution.Superseded.Count > 0
            ? $"Added “{candidate}” and removed {resolution.Superseded.Count} folder(s) now covered by it."
            : $"Added “{candidate}”.";
        RerunSearch();
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(WatchedFolderViewModel? folder)
    {
        if (folder is null) return;
        _index.RemoveRoot(folder.Root);
        WatchedFolders.Remove(folder);
        await PersistAsync();
        StatusText = $"Removed “{folder.Path}”.";
        RerunSearch();
    }

    [RelayCommand]
    private async Task RefreshFolderAsync(WatchedFolderViewModel? folder)
    {
        if (folder is null) return;
        folder.IsBusy = true;
        folder.Status = "Refreshing…";
        try
        {
            var result = await _crawler.CrawlAsync(folder.Path, null, CancellationToken.None);
            _index.ReplaceRoot(folder.Root, result);
            folder.Root = result.Root;
            folder.Status = "";
            StatusText = $"Refreshed “{folder.Path}”.";
            RerunSearch();
        }
        catch (Exception ex)
        {
            folder.Status = "Refresh failed";
            StatusText = $"Could not refresh “{folder.Path}”: {ex.Message}";
        }
        finally
        {
            folder.IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenResult(SearchResultViewModel? result)
    {
        if (result is not null) FileLauncher.Open(result.FullPath);
    }

    [RelayCommand]
    private void RevealResult(SearchResultViewModel? result)
    {
        if (result is not null) FileLauncher.RevealInExplorer(result.FullPath);
    }

    // --- Helpers ---

    private async Task CrawlAndAddAsync(string path)
    {
        try
        {
            var result = await _crawler.CrawlAsync(path, null, CancellationToken.None);
            _index.AddRoot(result);
            await Dispatcher.UIThread.InvokeAsync(() =>
                WatchedFolders.Add(new WatchedFolderViewModel(path, result.Root)));
        }
        catch (Exception ex)
        {
            StatusText = $"Could not crawl “{path}”: {ex.Message}";
        }
    }

    private Task PersistAsync() => _store.SaveAsync(WatchedFolders.Select(w => w.Path));

    private void RerunSearch() => _ = RunSearchAsync(SearchText);
}
