using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCrawler.Models;
using FileCrawler.Services;
using FileCrawler.ViewModels;
using Xunit;

namespace FileCrawler.Tests;

/// <summary>
/// A crawler that reports a time-out (with a suggested subfolder) until that subfolder is blocked, then reports a
/// clean crawl. Lets the UI tests drive the incomplete-crawl warning and its one-click block without real timing.
/// </summary>
internal sealed class TimeoutCrawler : IDirectoryCrawler
{
    public int Calls { get; private set; }

    public Task<CrawlResult> CrawlAsync(
        string rootPath,
        IReadOnlySet<string>? blockedFolders,
        IProgress<CrawlProgress>? progress,
        CancellationToken ct,
        TimeSpan? maxCrawlTime = null)
    {
        Calls++;
        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var suggested = Path.Combine(rootPath, "big");
        var root = new FileNode { Name = rootPath, IsDirectory = true, Children = Array.Empty<FileNode>() };
        var timedOut = blockedFolders is null || !blockedFolders.Contains(suggested);
        var all = new List<FileNode> { root };
        return Task.FromResult(new CrawlResult(
            root, all, Skipped: 0, BlockedItems: 0, BlockedItemsCapped: false,
            Exists: true, TimedOut: timedOut, SuggestedBlockPath: timedOut ? suggested : null));
    }
}

public class CrawlTimeoutUiTests : IDisposable
{
    private readonly string _tree;

    public CrawlTimeoutUiTests()
    {
        _tree = Path.Combine(Path.GetTempPath(), "FileCrawlerTimeoutUi_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tree);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tree, recursive: true); } catch { /* best effort */ }
    }

    private MainWindowViewModel BuildVm(IDirectoryCrawler crawler)
    {
        var index = new FileIndex();
        return new MainWindowViewModel(
            crawler, index, NoopStores.Watched, NoopStores.SearchState,
            new SearchService(index), new FakeFolderPicker(_tree), new FakeSubfolderBlockPicker(),
            new FakeConfirmationService(true), NoopStores.Settings);
    }

    [AvaloniaFact]
    public async Task A_timed_out_crawl_flags_the_folder_opens_the_sidebar_and_suggests_a_subfolder()
    {
        var vm = BuildVm(new TimeoutCrawler());

        await vm.AddFolderCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var folder = Assert.Single(vm.WatchedFolders);
        Assert.True(folder.IsIncomplete);
        Assert.EndsWith("big", folder.SuggestedBlockPath);
        Assert.Equal("Block “big”", folder.BlockSuggestionLabel);
        Assert.True(vm.IsSidebarExpanded); // surfaced so the user sees the warning
    }

    [AvaloniaFact]
    public async Task One_click_block_of_the_suggested_subfolder_recrawls_and_clears_the_warning()
    {
        var crawler = new TimeoutCrawler();
        var vm = BuildVm(crawler);

        await vm.AddFolderCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var folder = Assert.Single(vm.WatchedFolders);
        var callsAfterAdd = crawler.Calls;

        await vm.BlockSuggestedSubfolderCommand.ExecuteAsync(folder);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(folder.BlockedSubfolders, b => b.Path.EndsWith("big"));
        Assert.True(crawler.Calls > callsAfterAdd);   // it recrawled
        Assert.False(folder.IsIncomplete);            // blocking the culprit let the crawl finish
        Assert.Null(folder.SuggestedBlockPath);
    }
}

/// <summary>Shared no-op persistence so timeout UI tests don't touch %LOCALAPPDATA%.</summary>
internal static class NoopStores
{
    public static IWatchedFolderStore Watched => new WatchedStub();
    public static ISearchStateStore SearchState => new SearchStateStub();
    public static ISettingsStore Settings => new SettingsStub();

    private sealed class WatchedStub : IWatchedFolderStore
    {
        public Task<WatchedFolderState?> LoadAsync() => Task.FromResult<WatchedFolderState?>(WatchedFolderState.Empty);
        public Task SaveAsync(IEnumerable<string> folders, IEnumerable<string> blocked) => Task.CompletedTask;
    }

    private sealed class SearchStateStub : ISearchStateStore
    {
        public Task<SearchState?> LoadAsync() => Task.FromResult<SearchState?>(null);
        public Task SaveAsync(SearchState state) => Task.CompletedTask;
    }

    private sealed class SettingsStub : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
