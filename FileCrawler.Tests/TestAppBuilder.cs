using Avalonia;
using Avalonia.Headless;
using FileCrawler;
using FileCrawler.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace FileCrawler.Tests;

/// <summary>Builds the real <see cref="App"/> on the headless platform for UI tests.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
