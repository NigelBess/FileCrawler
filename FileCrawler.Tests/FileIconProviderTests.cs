using System;
using Avalonia.Headless.XUnit;
using FileCrawler.Services;
using Xunit;

namespace FileCrawler.Tests;

// [AvaloniaFact] so the headless platform is up — creating an Avalonia Bitmap requires it.
public class FileIconProviderTests
{
    [AvaloniaFact]
    public void File_icon_is_resolved_and_cached_per_extension()
    {
        if (!OperatingSystem.IsWindows()) return;

        var first = FileIconProvider.GetIcon("a.txt", isDirectory: false);
        var second = FileIconProvider.GetIcon("b.TXT", isDirectory: false);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public void Directory_icon_differs_from_file_icon()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dir = FileIconProvider.GetIcon("Projects", isDirectory: true);
        var file = FileIconProvider.GetIcon("report.pdf", isDirectory: false);

        Assert.NotNull(dir);
        Assert.NotSame(dir, file);
    }

    [AvaloniaFact]
    public void Unknown_extension_does_not_throw()
    {
        var icon = FileIconProvider.GetIcon("weird.zzzznoext", isDirectory: false);
        // Generic icon on Windows, null elsewhere — either way, no exception.
        _ = icon;
    }
}
