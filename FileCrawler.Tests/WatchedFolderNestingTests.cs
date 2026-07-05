using System.IO;
using System.Linq;
using FileCrawler.Services;
using Xunit;

namespace FileCrawler.Tests;

public class WatchedFolderNestingTests
{
    private static string P(params string[] parts) =>
        WatchedFolderNesting.Normalize(Path.Combine(parts));

    [Fact]
    public void LevelsBetween_returns_each_level_shallowest_first()
    {
        var owner = P("C:", "Watched");
        var target = P("C:", "Watched", "Projects", "2026");

        var levels = WatchedFolderNesting.LevelsBetween(target, owner);

        Assert.Equal(
            new[] { P("C:", "Watched", "Projects"), P("C:", "Watched", "Projects", "2026") },
            levels.ToArray());
    }

    [Fact]
    public void LevelsBetween_is_empty_when_target_is_the_owner()
    {
        var owner = P("C:", "Watched");
        Assert.Empty(WatchedFolderNesting.LevelsBetween(owner, owner));
    }

    [Fact]
    public void LevelsBetween_is_empty_when_target_is_outside_the_owner()
    {
        var owner = P("C:", "Watched");
        var target = P("C:", "Elsewhere", "Sub");
        Assert.Empty(WatchedFolderNesting.LevelsBetween(target, owner));
    }
}
