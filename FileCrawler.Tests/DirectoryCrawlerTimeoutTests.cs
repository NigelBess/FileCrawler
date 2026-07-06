using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Services;
using Xunit;

namespace FileCrawler.Tests;

/// <summary>Builds one large tree shared across the timeout tests (creating it per-test would dominate runtime).</summary>
public sealed class LargeTreeFixture : IDisposable
{
    public const int Dirs = 80;
    public const int FilesPerDir = 1000;

    public string Root { get; }
    public int TotalNodes => Dirs + Dirs * FilesPerDir + 1; // dirs + files + the root node

    public LargeTreeFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "FileCrawlerTimeout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Parallel.For(0, Dirs, d =>
        {
            var sub = Path.Combine(Root, $"dir{d:D2}");
            Directory.CreateDirectory(sub);
            for (var f = 0; f < FilesPerDir; f++)
                File.WriteAllText(Path.Combine(sub, $"file{f:D4}.txt"), "x");
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

public class DirectoryCrawlerTimeoutTests : IClassFixture<LargeTreeFixture>
{
    private readonly LargeTreeFixture _fixture;

    public DirectoryCrawlerTimeoutTests(LargeTreeFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task No_limit_crawls_the_whole_tree()
    {
        var result = await new DirectoryCrawler().CrawlAsync(_fixture.Root, null, null, CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Null(result.SuggestedBlockPath);
        Assert.Equal(_fixture.TotalNodes, result.AllNodes.Count);
    }

    [Fact]
    public async Task Hitting_the_time_limit_flags_the_result_keeps_partial_nodes_and_suggests_a_subfolder()
    {
        var result = await new DirectoryCrawler().CrawlAsync(
            _fixture.Root, null, null, CancellationToken.None, TimeSpan.FromMilliseconds(1));

        Assert.True(result.TimedOut);

        // Whatever was reached is still indexed, and the crawl stopped short of the full tree.
        Assert.True(result.AllNodes.Count >= 1);
        Assert.True(result.AllNodes.Count < _fixture.TotalNodes);

        // The suggestion is a real immediate subfolder of the crawled root — a one-click block candidate.
        Assert.NotNull(result.SuggestedBlockPath);
        Assert.Equal(_fixture.Root, Path.GetDirectoryName(result.SuggestedBlockPath));
        Assert.True(Directory.Exists(result.SuggestedBlockPath));
    }
}
