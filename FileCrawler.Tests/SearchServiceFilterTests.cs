using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;
using FileCrawler.Services;
using Xunit;

namespace FileCrawler.Tests;

public class SearchServiceFilterTests
{
    /// <summary>Serves a fixed node list; no crawling needed.</summary>
    private sealed class FakeIndex : IFileIndex
    {
        public FakeIndex(params FileNode[] nodes) => AllNodes = nodes;
        public IReadOnlyList<FileNode> Roots => Array.Empty<FileNode>();
        public IReadOnlyList<FileNode> AllNodes { get; }
        public void AddRoot(CrawlResult result) => throw new NotSupportedException();
        public void RemoveRoot(FileNode root) => throw new NotSupportedException();
        public void ReplaceRoot(FileNode oldRoot, CrawlResult newResult) => throw new NotSupportedException();
    }

    private static FileNode File(string name, long size = 0, DateTime? modifiedUtc = null) =>
        new() { Name = name, SizeBytes = size, ModifiedUtc = modifiedUtc ?? default, IsDirectory = false };

    private static FileNode Dir(string name, long size = 0, DateTime? modifiedUtc = null) =>
        new() { Name = name, SizeBytes = size, ModifiedUtc = modifiedUtc ?? default, IsDirectory = true };

    private static Task<SearchResults> SearchAsync(SearchCriteria criteria, params FileNode[] nodes) =>
        new SearchService(new FakeIndex(nodes)).SearchAsync(criteria, CancellationToken.None);

    [Fact]
    public async Task Extension_filter_matches_case_insensitively()
    {
        var results = await SearchAsync(
            new SearchCriteria("", Extensions: [".png"]),
            File("Photo.PNG"), File("notes.txt"));

        Assert.Equal(["Photo.PNG"], results.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Extension_filter_excludes_extensionless_files_and_folders_when_folders_off()
    {
        var results = await SearchAsync(
            new SearchCriteria("", Extensions: [".png"], IncludeFolders: false),
            Dir("gallery.png"), File("README"), File("cat.png"));

        Assert.Equal(["cat.png"], results.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Folders_are_included_alongside_matching_files_when_folders_on()
    {
        var results = await SearchAsync(
            new SearchCriteria("", Extensions: [".png"], IncludeFolders: true),
            Dir("gallery"), File("notes.txt"), File("cat.png"));

        Assert.Equal(["gallery", "cat.png"], results.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Size_bounds_are_inclusive_and_directories_participate()
    {
        var results = await SearchAsync(
            new SearchCriteria("", MinSizeBytes: 100, MaxSizeBytes: 200),
            File("tiny.txt", size: 99),
            File("min.txt", size: 100),
            Dir("big folder", size: 200),
            File("huge.txt", size: 201));

        Assert.Equal(["min.txt", "big folder"], results.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Date_window_is_after_inclusive_before_exclusive()
    {
        var after = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var results = await SearchAsync(
            new SearchCriteria("", ModifiedAfterUtc: after, ModifiedBeforeUtc: before),
            File("old.txt", modifiedUtc: after.AddSeconds(-1)),
            File("boundary-start.txt", modifiedUtc: after),
            File("boundary-end.txt", modifiedUtc: before),
            File("inside.txt", modifiedUtc: after.AddDays(10)));

        Assert.Equal(["boundary-start.txt", "inside.txt"], results.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Folders_toggle_selects_files_or_folders()
    {
        FileNode[] nodes = [File("a.txt"), Dir("stuff")];

        // All files (Extensions null), folders off => files only.
        var files = await SearchAsync(new SearchCriteria("", Extensions: null, IncludeFolders: false), nodes);
        // No files (empty allowlist), folders on => folders only.
        var folders = await SearchAsync(new SearchCriteria("", Extensions: [], IncludeFolders: true), nodes);

        Assert.Equal(["a.txt"], files.Items.Select(n => n.Name));
        Assert.Equal(["stuff"], folders.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Empty_query_with_filters_returns_matches_but_without_filters_returns_nothing()
    {
        FileNode[] nodes = [File("a.txt")];

        var browse = await SearchAsync(new SearchCriteria("", Extensions: [".txt"]), nodes);
        var empty = await SearchAsync(new SearchCriteria(""), nodes);

        Assert.Single(browse.Items);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Filters_compose_with_the_name_query()
    {
        var results = await SearchAsync(
            new SearchCriteria("report", Extensions: [".pdf"]),
            File("report.pdf"), File("report.txt"), File("summary.pdf"));

        Assert.Equal(["report.pdf"], results.Items.Select(n => n.Name));
    }

    [Fact]
    public async Task Result_cap_is_honored_with_filters_active()
    {
        var nodes = Enumerable.Range(0, SearchService.MaxResults + 5)
            .Select(i => File($"file{i}.txt"))
            .ToArray();

        var results = await SearchAsync(new SearchCriteria("", Extensions: [".txt"]), nodes);

        Assert.Equal(SearchService.MaxResults, results.Items.Count);
        Assert.True(results.Capped);
    }
}
