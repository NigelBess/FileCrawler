using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCrawler.Models;
using FileCrawler.Views;
using Xunit;

namespace FileCrawler.Tests;

public class SettingsTests
{
    [Fact]
    public void MaxCrawlTime_is_the_seconds_as_a_timespan_when_positive()
    {
        Assert.Equal(10, AppSettings.DefaultMaxCrawlSeconds);
        Assert.Equal(15, new AppSettings { MaxCrawlSeconds = 15 }.MaxCrawlTime!.Value.TotalSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_limit_means_unlimited(double seconds)
    {
        Assert.Null(new AppSettings { MaxCrawlSeconds = seconds }.MaxCrawlTime);
    }

    [AvaloniaFact]
    public void Settings_dialog_loads_and_shows_the_current_limit()
    {
        // Constructing the dialog runs its XAML (catches typos like a bad icon Kind) and seeds the input.
        var dialog = new SettingsDialog(new AppSettings { MaxCrawlSeconds = 25 });

        var input = dialog.FindControl<NumericUpDown>("MaxCrawlSecondsInput");
        Assert.NotNull(input);
        Assert.Equal(25m, input!.Value);
    }
}
