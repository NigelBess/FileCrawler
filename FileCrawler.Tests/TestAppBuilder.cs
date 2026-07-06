using Avalonia;
using Avalonia.Headless;
using FileCrawler;
using FileCrawler.Tests;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// Run test collections serially. The UI tests depend on real-time debounce/search windows and share a single
// headless Avalonia app, so overlapping them (with each other or with CPU/disk-heavy crawl tests) makes their
// timing assertions flaky.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FileCrawler.Tests;

/// <summary>Builds the real <see cref="App"/> on the headless platform for UI tests. Skia replaces the
/// default stub drawing so bitmap decoding (thumbnails, icons) behaves as it does in the real app.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
