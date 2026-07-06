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
using FileCrawler.Utilities;

namespace FileCrawler.ViewModels;

/// <summary>The result column the list is ordered by.</summary>
public enum ResultSortColumn { Name, Path, Extension, Size, Modified }

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const int SearchDebounceMs = 200;

    private readonly IDirectoryCrawler _crawler;
    private readonly IFileIndex _index;
    private readonly IWatchedFolderStore _store;
    private readonly ISearchStateStore _searchStateStore;
    private readonly ISearchService _search;
    private readonly IFolderPicker _picker;
    private readonly ISubfolderBlockPicker _blockPicker;
    private readonly IConfirmationService _confirm;
    private readonly ISettingsStore _settingsStore;
    private readonly ISettingsEditor _settingsEditor;

    private AppSettings _settings;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _resultsCts;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Add a folder to start searching.";
    [ObservableProperty] private string _resultsSummary = "";
    [ObservableProperty] private string _blockedSummary = "";
    [ObservableProperty] private bool _isSidebarExpanded;

    [ObservableProperty] private ResultSortColumn _sortColumn = ResultSortColumn.Name;
    [ObservableProperty] private bool _sortDescending;

    public ObservableCollection<WatchedFolderViewModel> WatchedFolders { get; } = new();
    public ObservableCollection<SearchResultViewModel> Results { get; } = new();
    public SearchFiltersViewModel Filters { get; } = new();

    // --- Sortable column headers ---

    // Header captions carry an arrow for the active sort column so the list header doubles as the indicator.
    public string NameHeader => "Name" + Indicator(ResultSortColumn.Name);
    public string PathHeader => "Path" + Indicator(ResultSortColumn.Path);
    public string SizeHeader => "Size" + Indicator(ResultSortColumn.Size);
    public string ModifiedHeader => "Modified" + Indicator(ResultSortColumn.Modified);
    // The type/extension sort lives on the narrow icon column, so it shows just the arrow (no caption).
    public string ExtensionIndicator => SortColumn == ResultSortColumn.Extension ? Arrow() : "";

    private string Indicator(ResultSortColumn column) => SortColumn == column ? " " + Arrow() : "";
    private string Arrow() => SortDescending ? "▾" : "▴";

    partial void OnSortColumnChanged(ResultSortColumn value) => RaiseHeaderChanges();
    partial void OnSortDescendingChanged(bool value) => RaiseHeaderChanges();

    private void RaiseHeaderChanges()
    {
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(PathHeader));
        OnPropertyChanged(nameof(SizeHeader));
        OnPropertyChanged(nameof(ModifiedHeader));
        OnPropertyChanged(nameof(ExtensionIndicator));
    }

    /// <summary>Design-time constructor (also used by the XAML previewer).</summary>
    public MainWindowViewModel()
    {
        _crawler = new DirectoryCrawler();
        _index = new FileIndex();
        _store = new WatchedFolderStore();
        _searchStateStore = new SearchStateStore();
        _search = new SearchService(_index);
        _picker = new StorageFolderPicker(() => null);
        _blockPicker = new DialogSubfolderBlockPicker(() => null);
        _confirm = new DialogConfirmationService(() => null);
        _settingsStore = new SettingsStore();
        _settingsEditor = new DialogSettingsEditor(() => null);
        _settings = new AppSettings();
        Filters.CriteriaChanged += RerunSearch;
    }

    public MainWindowViewModel(
        IDirectoryCrawler crawler,
        IFileIndex index,
        IWatchedFolderStore store,
        ISearchStateStore searchStateStore,
        ISearchService search,
        IFolderPicker picker,
        ISubfolderBlockPicker blockPicker,
        IConfirmationService confirm,
        ISettingsStore? settingsStore = null,
        ISettingsEditor? settingsEditor = null)
    {
        _crawler = crawler;
        _index = index;
        _store = store;
        _searchStateStore = searchStateStore;
        _search = search;
        _picker = picker;
        _blockPicker = blockPicker;
        _confirm = confirm;
        _settingsStore = settingsStore ?? new SettingsStore();
        _settingsEditor = settingsEditor ?? new DialogSettingsEditor(() => null);
        _settings = _settingsStore.Load();
        Filters.CriteriaChanged += RerunSearch;
    }

    /// <summary>Loads persisted watched folders and crawls them concurrently. Call once after the window shows.</summary>
    public async Task InitializeAsync()
    {
        // Restore the saved filters first (silently — no search yet), then crawl, then replay the search term
        // last so the search runs against a populated index.
        var searchState = await _searchStateStore.LoadAsync();
        if (searchState is not null) Filters.RestoreState(searchState.Filters);

        // A null load means the workspace has never been configured (true first run): seed the user's standard
        // content folders so search works out of the box. A saved-but-empty workspace loads as Empty (not null),
        // so a user who removed every folder isn't re-seeded on the next launch.
        var loaded = await _store.LoadAsync();
        var isFirstRun = loaded is null;
        var saved = loaded ?? new WatchedFolderState(StandardUserFolders.Resolve(), Array.Empty<string>());

        await LoadFoldersAsync(saved);

        // Persist the seeded folders so the next launch is no longer treated as a first run (and honors any
        // folders the user later removes). Writing an empty set is still correct: it records that setup ran.
        if (isFirstRun) await PersistAsync();

        // Replay the last search now that the index is ready. Setting a non-empty term triggers the search;
        // for an empty term we rerun explicitly so restored filters still take effect.
        if (searchState is not null)
        {
            if (!string.IsNullOrEmpty(searchState.SearchText))
                SearchText = searchState.SearchText;
            else
                RerunSearch();
        }
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
            var rows = results.Items.Select(node => new SearchResultViewModel(node, lifetime));
            foreach (var row in SortRows(rows)) Results.Add(row);

            ResultsSummary = results.Capped
                ? $"Showing first {results.Items.Count:N0} of many — refine your search."
                : string.IsNullOrWhiteSpace(query)
                    ? criteria.HasFilters
                        ? $"{results.Items.Count:N0} filtered item(s)."
                        : $"{results.Items.Count:N0} item(s)."
                    : $"{results.Items.Count:N0} result(s).";

            // A coverage caveat, not a match count: blocked folders are never crawled, so this total isn't
            // filtered by the query. Suppress it when the filters exclude everything anyway ("Select none") —
            // there it's pure noise.
            var blocked = _index.BlockedItems;
            BlockedSummary = criteria.MatchesNothing || blocked == 0
                ? ""
                : _index.BlockedItemsCapped
                    ? $"Over {blocked:N0} items in blocked folders aren’t searched."
                    : $"{blocked:N0} item(s) in blocked folders aren’t searched.";

            // The debounce already coalesced rapid keystrokes/filter clicks, so this is the last run — a good
            // point to persist the term and filters (fire-and-forget; writing happens off the UI thread).
            _ = _searchStateStore.SaveAsync(new SearchState(query, Filters.CaptureState()));
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
        SurfaceIncompleteCrawls();
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
    private async Task RefreshAllAsync()
    {
        if (WatchedFolders.Count == 0) return;

        IsBusy = true;
        StatusText = $"Refreshing {WatchedFolders.Count} folder(s)…";
        await Task.WhenAll(WatchedFolders.ToList().Select(RecrawlFolderAsync));
        IsBusy = false;

        StatusText = $"Ready — {_index.AllNodes.Count:N0} items indexed across {WatchedFolders.Count} folder(s).";
        SurfaceIncompleteCrawls();
        RerunSearch();
    }

    /// <summary>
    /// Resets the workspace to a clean slate: drops every watched folder and block, clears the search term and
    /// all filters, then re-seeds the user's standard content folders (exactly as on first run) and crawls them.
    /// </summary>
    [RelayCommand]
    private async Task ResetAsync()
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Reset FileCrawler?",
            "This removes all watched folders, blocked subfolders, filters and the current search, then " +
            "restores the standard folders (Documents, Desktop, Downloads, and so on). This can't be undone.",
            "Reset");
        if (!confirmed) return;

        // Drop every watched root from the index and the sidebar.
        foreach (var folder in WatchedFolders.ToList()) _index.RemoveRoot(folder.Root);
        WatchedFolders.Clear();

        // Back to an empty search and default filters (this also clears any per-search folder blocks).
        SearchText = "";
        Filters.ClearFiltersCommand.Execute(null);

        // Re-seed the standard folders exactly as a first run would, then persist and search.
        await LoadFoldersAsync(new WatchedFolderState(StandardUserFolders.Resolve(), Array.Empty<string>()));
        await PersistAsync();
        RerunSearch();
    }

    /// <summary>Opens the settings editor and persists any changes (they apply to subsequent crawls).</summary>
    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var updated = await _settingsEditor.EditAsync(_settings);
        if (updated is null) return;

        _settings = updated;
        _settingsStore.Save(updated);
        StatusText = _settings.MaxCrawlTime is { } limit
            ? $"Settings saved. Folders stop crawling after {limit.TotalSeconds:N0} s."
            : "Settings saved. Folder crawl time is now unlimited.";
    }

    /// <summary>
    /// One-click block of the subfolder suggested after a timed-out crawl (the largest crawled branch) from every
    /// search, then recrawls — freeing the crawl to finish within the time limit. No-op without a suggestion.
    /// </summary>
    [RelayCommand]
    private async Task BlockSuggestedSubfolderAsync(WatchedFolderViewModel? folder)
    {
        if (folder?.SuggestedBlockPath is not { Length: > 0 } blockPath) return;
        await AddBlockAsync(folder, blockPath);
    }

    /// <summary>
    /// Blocks a subfolder of <paramref name="result"/> from <em>every</em> search: adds a permanent watched-folder
    /// block and recrawls so the folder is never indexed. Prompts for which folder level to block first.
    /// </summary>
    [RelayCommand]
    private async Task BlockSubfolderFromAllSearchesAsync(SearchResultViewModel? result)
    {
        var (owner, blockPath) = await PickBlockLevelAsync(result);
        if (owner is null || blockPath is null) return;
        await AddBlockAsync(owner, blockPath);
    }

    /// <summary>
    /// Blocks a subfolder of <paramref name="result"/> from <em>this</em> search only: adds a per-search filter
    /// that excludes the folder at search time (no recrawl; removable via its X in the filter bar). Prompts for
    /// which folder level to block first, using the same dialog as the all-searches block.
    /// </summary>
    [RelayCommand]
    private async Task BlockSubfolderFromThisSearchAsync(SearchResultViewModel? result)
    {
        var (_, blockPath) = await PickBlockLevelAsync(result);
        if (blockPath is null) return;
        Filters.AddSearchBlock(blockPath);
        StatusText = $"Blocked “{blockPath}” from this search.";
    }

    /// <summary>
    /// Prompts the user to pick which folder level to block for <paramref name="result"/> — one choice per level
    /// of the hierarchy between the owning watched root and the folder the result lives in (its parent directory
    /// for a file, or the folder itself for a directory). Returns the owning watched folder and the chosen path,
    /// or a null <c>blockPath</c> if the result can't be blocked or the user cancels. Reports why via StatusText.
    /// </summary>
    private async Task<(WatchedFolderViewModel? owner, string? blockPath)> PickBlockLevelAsync(
        SearchResultViewModel? result)
    {
        if (result is null) return (null, null);

        var target = result.IsDirectory
            ? result.FullPath
            : System.IO.Path.GetDirectoryName(result.FullPath);
        if (string.IsNullOrEmpty(target)) return (null, null);

        var targetPath = WatchedFolderNesting.Normalize(target);

        var owner = WatchedFolders.FirstOrDefault(
            w => WatchedFolderNesting.IsSameOrDescendant(targetPath, w.Path));
        if (owner is null)
        {
            StatusText = $"“{targetPath}” is not inside any watched folder.";
            return (null, null);
        }

        // Each folder level between the watched root (exclusive) and the result's folder (inclusive) is a
        // block candidate. Empty when the result sits directly in the root — nothing below it to block.
        var levels = WatchedFolderNesting.LevelsBetween(targetPath, owner.Path);
        if (levels.Count == 0)
        {
            StatusText = $"“{targetPath}” is a watched folder — use Remove instead of blocking.";
            return (owner, null);
        }

        var blockPath = await _blockPicker.PickAsync(levels);
        return string.IsNullOrEmpty(blockPath) ? (owner, null) : (owner, blockPath);
    }

    /// <summary>Prompts for a subfolder of <paramref name="owner"/> and blocks it from results.</summary>
    [RelayCommand]
    private async Task AddBlockedSubfolderAsync(WatchedFolderViewModel? owner)
    {
        if (owner is null) return;

        var picked = await _picker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(picked)) return;

        var blockPath = WatchedFolderNesting.Normalize(picked);

        if (string.Equals(owner.Path, blockPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"“{blockPath}” is a watched folder — use Remove instead of blocking.";
            return;
        }

        if (!WatchedFolderNesting.IsSameOrDescendant(blockPath, owner.Path))
        {
            StatusText = $"“{blockPath}” is not inside “{owner.Path}”.";
            return;
        }

        await AddBlockAsync(owner, blockPath);
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

    /// <summary>
    /// Sorts the results by <paramref name="column"/>. Clicking the active column again flips the direction;
    /// clicking a new column starts ascending. Re-sorts the current rows in place — no re-search — so cached
    /// thumbnails survive.
    /// </summary>
    [RelayCommand]
    private void SortBy(string column)
    {
        if (!Enum.TryParse<ResultSortColumn>(column, out var target)) return;

        if (target == SortColumn)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = target;
            SortDescending = false;
        }

        var sorted = SortRows(Results.ToList()).ToList();
        Results.Clear();
        foreach (var row in sorted) Results.Add(row);
    }

    private IEnumerable<SearchResultViewModel> SortRows(IEnumerable<SearchResultViewModel> rows) =>
        SortColumn switch
        {
            ResultSortColumn.Name => Order(rows, r => r.Node.Name, StringComparer.OrdinalIgnoreCase),
            ResultSortColumn.Path => Order(rows, r => r.FullPath, StringComparer.OrdinalIgnoreCase),
            ResultSortColumn.Extension =>
                Order(rows, r => System.IO.Path.GetExtension(r.Node.Name), StringComparer.OrdinalIgnoreCase),
            ResultSortColumn.Size => Order(rows, r => r.Node.SizeBytes),
            ResultSortColumn.Modified => Order(rows, r => r.Node.ModifiedUtc),
            _ => rows,
        };

    private IEnumerable<SearchResultViewModel> Order<TKey>(
        IEnumerable<SearchResultViewModel> rows, Func<SearchResultViewModel, TKey> key, IComparer<TKey>? comparer = null) =>
        SortDescending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

    // --- Helpers ---

    /// <summary>
    /// Crawls every folder in <paramref name="state"/> concurrently (each honoring the blocks that fall under it)
    /// and adds them to the index and sidebar, driving the busy indicator and status text. No-op for an empty set.
    /// Shared by first-run/persisted load and by Reset.
    /// </summary>
    private async Task LoadFoldersAsync(WatchedFolderState state)
    {
        if (state.Folders.Count == 0) return;

        IsBusy = true;
        StatusText = $"Crawling {state.Folders.Count} folder(s)…";

        // Assign each block to the watched root that contains it.
        await Task.WhenAll(state.Folders.Select(path =>
            CrawlAndAddAsync(path, BlocksUnder(path, state.Blocked))));

        IsBusy = false;
        StatusText = WatchedFolders.Count == 0
            ? "Add a folder to start searching."
            : $"Ready — {_index.AllNodes.Count:N0} items indexed across {WatchedFolders.Count} folder(s).";
        SurfaceIncompleteCrawls();
    }

    /// <summary>Crawls <paramref name="path"/> excluding <paramref name="blocked"/>, indexes it, and adds the row.</summary>
    private async Task CrawlAndAddAsync(string path, IReadOnlyList<string> blocked)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _crawler.CrawlAsync(path, ToSet(blocked), null, CancellationToken.None, _settings.MaxCrawlTime);
            sw.Stop();
            _index.AddRoot(result);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = new WatchedFolderViewModel(path, result.Root)
                {
                    // AllNodes includes the root itself, which isn't a "file or subfolder".
                    ItemCount = Math.Max(0, result.AllNodes.Count - 1),
                    LoadTime = sw.Elapsed,
                    IsMissing = !result.Exists,
                    IsIncomplete = result.TimedOut,
                    SuggestedBlockPath = result.SuggestedBlockPath,
                };
                foreach (var b in blocked) vm.BlockedSubfolders.Add(new BlockedFolderViewModel(b));
                WatchedFolders.Add(vm);
                FloatMissingFoldersToTop();
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _crawler.CrawlAsync(folder.Path, blocked, null, CancellationToken.None, _settings.MaxCrawlTime);
            sw.Stop();
            _index.ReplaceRoot(folder.Root, result);
            folder.Root = result.Root;
            folder.ItemCount = Math.Max(0, result.AllNodes.Count - 1);
            folder.LoadTime = sw.Elapsed;
            folder.IsMissing = !result.Exists;
            folder.IsIncomplete = result.TimedOut;
            folder.SuggestedBlockPath = result.SuggestedBlockPath;
            folder.Status = "";
            FloatMissingFoldersToTop();
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

    /// <summary>
    /// Adds <paramref name="blockPath"/> as a block under <paramref name="owner"/> (deduping nested blocks),
    /// then recrawls and persists. Assumes <paramref name="blockPath"/> is a subfolder of the owner, not the root.
    /// </summary>
    private async Task AddBlockAsync(WatchedFolderViewModel owner, string blockPath)
    {
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

    /// <summary>Recrawls <paramref name="folder"/> after its block set changed, persists, and re-searches.</summary>
    private async Task ApplyBlockChangeAsync(WatchedFolderViewModel folder)
    {
        IsBusy = true;
        await RecrawlFolderAsync(folder);
        IsBusy = false;
        await PersistAsync();
        SurfaceIncompleteCrawls();
        RerunSearch();
    }

    /// <summary>
    /// If any watched folder's crawl timed out, expands the sidebar (so the in-row yellow warning and its
    /// one-click block button are visible) and reflects it in the status line. No-op when everything finished.
    /// </summary>
    private void SurfaceIncompleteCrawls()
    {
        var incomplete = WatchedFolders.Count(f => f.IsIncomplete);
        if (incomplete == 0) return;

        IsSidebarExpanded = true;
        StatusText = incomplete == 1
            ? "A folder took too long to crawl — its results may be incomplete. Open the sidebar to block a large subfolder or raise the limit in Settings."
            : $"{incomplete} folders took too long to crawl — their results may be incomplete. Open the sidebar to block large subfolders or raise the limit in Settings.";
    }

    /// <summary>
    /// Reorders <see cref="WatchedFolders"/> so folders whose path is missing on disk sit at the top, where the
    /// user can't miss the warning. Stable: within each group (missing / present) the existing relative order is
    /// preserved, and it moves only the items actually out of place, so an unchanged list stays put.
    /// </summary>
    private void FloatMissingFoldersToTop()
    {
        var desired = WatchedFolders
            .Select((folder, index) => (folder, index))
            .OrderBy(x => x.folder.IsMissing ? 0 : 1)
            .ThenBy(x => x.index)
            .Select(x => x.folder)
            .ToList();

        for (var target = 0; target < desired.Count; target++)
        {
            var current = WatchedFolders.IndexOf(desired[target]);
            if (current != target) WatchedFolders.Move(current, target);
        }
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
