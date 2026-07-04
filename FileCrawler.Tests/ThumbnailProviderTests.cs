using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCrawler.Models;
using FileCrawler.Services;
using FileCrawler.ViewModels;
using Xunit;

namespace FileCrawler.Tests;

// [AvaloniaFact] so the headless (Skia-backed) platform is up — decoding needs a real render interface.
public class ThumbnailProviderTests : IDisposable
{
    private readonly string _dir;

    public ThumbnailProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FileCrawlerThumbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Writes a real PNG of the given size (contents are transparent pixels; only decoding matters).</summary>
    private string WritePng(string name, int width = 200, int height = 100)
    {
        using var bitmap = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        var path = Path.Combine(_dir, name);
        bitmap.Save(path);
        return path;
    }

    [Fact]
    public void Only_decodable_image_extensions_are_thumbnailable()
    {
        Assert.True(ThumbnailProvider.IsThumbnailable("photo.png"));
        Assert.True(ThumbnailProvider.IsThumbnailable("photo.JPG"));
        Assert.False(ThumbnailProvider.IsThumbnailable("vector.svg"));
        Assert.False(ThumbnailProvider.IsThumbnailable("notes.txt"));
        Assert.False(ThumbnailProvider.IsThumbnailable("no-extension"));
    }

    [AvaloniaFact]
    public async Task Png_is_decoded_down_to_the_thumbnail_width()
    {
        var path = WritePng("wide.png");

        var thumb = await ThumbnailProvider.GetThumbnailAsync(path, DateTime.UtcNow, CancellationToken.None);

        Assert.NotNull(thumb);
        Assert.Equal(ThumbnailProvider.DecodeWidth, thumb!.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task Same_file_returns_the_cached_instance()
    {
        var path = WritePng("cached.png");
        var modified = DateTime.UtcNow;

        var first = await ThumbnailProvider.GetThumbnailAsync(path, modified, CancellationToken.None);
        var second = await ThumbnailProvider.GetThumbnailAsync(path, modified, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public async Task Corrupt_image_yields_null_instead_of_throwing()
    {
        var path = Path.Combine(_dir, "corrupt.png");
        await File.WriteAllTextAsync(path, "this is not a png");

        var thumb = await ThumbnailProvider.GetThumbnailAsync(path, DateTime.UtcNow, CancellationToken.None);

        Assert.Null(thumb);
    }

    [AvaloniaFact]
    public async Task Cancelled_request_never_touches_the_file()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A nonexistent path proves cancellation short-circuits before any IO (otherwise it would cache null).
        var missing = Path.Combine(_dir, "never-created.png");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ThumbnailProvider.GetThumbnailAsync(missing, DateTime.UtcNow, cts.Token));
    }
}

public class SearchResultThumbnailTests : IDisposable
{
    private readonly string _dir;

    public SearchResultThumbnailTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FileCrawlerRowThumbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WritePng(string name)
    {
        using var bitmap = new WriteableBitmap(
            new PixelSize(120, 80), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var path = Path.Combine(_dir, name);
        bitmap.Save(path);
        return path;
    }

    /// <summary>A root node's Name is its full path, matching how watched-folder roots are stored.</summary>
    private static FileNode NodeFor(string path) =>
        new() { Name = path, IsDirectory = false, ModifiedUtc = DateTime.UtcNow };

    [AvaloniaFact]
    public async Task Reading_Image_swaps_the_icon_for_a_thumbnail()
    {
        var vm = new SearchResultViewModel(NodeFor(WritePng("row.png")));

        var initial = vm.Image; // first read starts the background load

        var swapped = false;
        vm.PropertyChanged += (_, e) => swapped |= e.PropertyName == nameof(vm.Image);
        for (var i = 0; i < 200 && !swapped; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(swapped, "thumbnail never replaced the icon");
        Assert.NotNull(vm.Image);
        Assert.NotSame(initial, vm.Image);
        Assert.Equal(ThumbnailProvider.DecodeWidth, vm.Image!.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task Superseded_row_keeps_its_icon()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var vm = new SearchResultViewModel(NodeFor(WritePng("stale.png")), cts.Token);

        var initial = vm.Image;
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(initial, vm.Image);
    }
}
