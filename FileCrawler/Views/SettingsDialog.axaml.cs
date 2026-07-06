using Avalonia.Controls;
using Avalonia.Interactivity;
using FileCrawler.Models;

namespace FileCrawler.Views
{
    /// <summary>Edits <see cref="AppSettings"/>. Closes with the updated settings on Save, or null on cancel/dismiss.</summary>
    public partial class SettingsDialog : Window
    {
        // Parameterless ctor for the XAML designer / previewer.
        public SettingsDialog() => InitializeComponent();

        public SettingsDialog(AppSettings settings) : this()
        {
            MaxCrawlSecondsInput.Value = (decimal)settings.MaxCrawlSeconds;
        }

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            var seconds = (double)(MaxCrawlSecondsInput.Value ?? (decimal)AppSettings.DefaultMaxCrawlSeconds);
            Close(new AppSettings { MaxCrawlSeconds = seconds });
        }

        private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    }
}
