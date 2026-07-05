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
    [ObservableProperty] private bool _isSidebarExpanded;

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
        if (saved.Folders.Count == 0) return;

        IsBusy = true;
        StatusText = $"Crawling {saved.Folders.Count} folder(s)…";

        // Assign each persisted block to the watched root that contains it.
        await Task.WhenAll(saved.Folders.Select(path =>
            CrawlAndAddAsync(path, BlocksUnder(path, saved.Blocked))));

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

        // Remove any existing roots now nested inside the new one, carrying their blocks onto the new root.
        var carriedBlocks = new List<string>();
        foreach (var superseded in resolution.Superseded)
        {
            var vm = WatchedFolders.FirstOrDefault(w => w.Path == superseded);
            if (vm is not null)
            {
                carriedBlocks.AddRange(vm.BlockedSubfolders.Select(b => b.Path));
                _index.RemoveRoot(vm.Root);
                WatchedFolders.Remove(vm);
            }
        }

        IsBusy = true;
        await CrawlAndAddAsync(candidate, carriedBlocks);
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
        if (await RecrawlFolderAsync(folder))
        {
            StatusText = $"Refreshed “{folder.Path}”.";
            RerunSearch();
        }
    }

    /// <summary>
    /// Blocks the subfolder that <paramref name="result"/> lives in (its parent directory for a file, or the
    /// folder itself for a directory) so its contents are never included, then recrawls the owning root. The
    /// block is nested under the watched folder that contains it.
    /// </summary>
    [RelayCommand]
    private async Task BlockSubfolderAsync(SearchResultViewModel? result)
    {
        if (result is null) return;

        var target = result.IsDirectory
            ? result.FullPath
            : System.IO.Path.GetDirectoryName(result.FullPath);
        if (string.IsNullOrEmpty(target)) return;

        var blockPath = WatchedFolderNesting.Normalize(target);

        var owner = WatchedFolders.FirstOrDefault(
            w => WatchedFolderNesting.IsSameOrDescendant(blockPath, w.Path));
        if (owner is null)
        {
            StatusText = $"“{blockPath}” is not inside any watched folder.";
            return;
        }

        // Blocking a watched root would empty it out silently; direct the user to remove it instead.
        if (string.Equals(owner.Path, blockPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"“{blockPath}” is a watched folder — use Remove instead of blocking.";
            return;
        }

        var existing = owner.BlockedSubfolders.Select(b => b.Path).ToList();
        var resolution = WatchedFolderNesting.Resolve(blockPath, existing);
        if (!resolution.CanAdd)
        {
            StatusText = $"“{blockPath}” is already blocked (covered by “{resolution.CoveredBy}”).";
            return;
        }

        // Drop any already-blocked subfolders now covered by this (broader) block.
        foreach (var superseded in resolution.Superseded)
        {
            var vm = owner.BlockedSubfolders.FirstOrDefault(b => b.Path == superseded);
            if (vm is not null) owner.BlockedSubfolders.Remove(vm);
        }
        owner.BlockedSubfolders.Add(new BlockedFolderViewModel(blockPath));

        await ApplyBlockChangeAsync(owner);
        StatusText = $"Blocked “{blockPath}”.";
    }

    [RelayCommand]
    private async Task UnblockSubfolderAsync(BlockedFolderViewModel? blocked)
    {
        if (blocked is null) return;

        var owner = WatchedFolders.FirstOrDefault(w => w.BlockedSubfolders.Contains(blocked));
        if (owner is null) return;

        owner.BlockedSubfolders.Remove(blocked);
        await ApplyBlockChangeAsync(owner);
        StatusText = $"Unblocked “{blocked.Path}”.";
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

    /// <summary>Crawls <paramref name="path"/> excluding <paramref name="blocked"/>, indexes it, and adds the row.</summary>
    private async Task CrawlAndAddAsync(string path, IReadOnlyList<string> blocked)
    {
        try
        {
            var result = await _crawler.CrawlAsync(path, ToSet(blocked), null, CancellationToken.None);
            _index.AddRoot(result);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = new WatchedFolderViewModel(path, result.Root);
                foreach (var b in blocked) vm.BlockedSubfolders.Add(new BlockedFolderViewModel(b));
                WatchedFolders.Add(vm);
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not crawl “{path}”: {ex.Message}";
        }
    }

    /// <summary>Recrawls a watched root (honoring its own blocks) and swaps it into the index. Returns success.</summary>
    private async Task<bool> RecrawlFolderAsync(WatchedFolderViewModel folder)
    {
        folder.IsBusy = true;
        folder.Status = "Refreshing…";
        try
        {
            var blocked = ToSet(folder.BlockedSubfolders.Select(b => b.Path));
            var result = await _crawler.CrawlAsync(folder.Path, blocked, null, CancellationToken.None);
            _index.ReplaceRoot(folder.Root, result);
            folder.Root = result.Root;
            folder.Status = "";
            return true;
        }
        catch (Exception ex)
        {
            folder.Status = "Refresh failed";
            StatusText = $"Could not refresh “{folder.Path}”: {ex.Message}";
            return false;
        }
        finally
        {
            folder.IsBusy = false;
        }
    }

    /// <summary>Recrawls <paramref name="folder"/> after its block set changed, persists, and re-searches.</summary>
    private async Task ApplyBlockChangeAsync(WatchedFolderViewModel folder)
    {
        IsBusy = true;
        await RecrawlFolderAsync(folder);
        IsBusy = false;
        await PersistAsync();
        RerunSearch();
    }

    /// <summary>The blocked paths from <paramref name="blocked"/> that fall under watched root <paramref name="path"/>.</summary>
    private static List<string> BlocksUnder(string path, IEnumerable<string> blocked) =>
        blocked.Where(b => WatchedFolderNesting.IsSameOrDescendant(b, path)).ToList();

    private static IReadOnlySet<string> ToSet(IEnumerable<string> paths) =>
        paths.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private Task PersistAsync() =>
        _store.SaveAsync(
            WatchedFolders.Select(w => w.Path),
            WatchedFolders.SelectMany(w => w.BlockedSubfolders).Select(b => b.Path));

    private void RerunSearch() => _ = RunSearchAsync(SearchText);
}
