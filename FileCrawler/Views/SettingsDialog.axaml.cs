using Avalonia.Controls;
using Avalonia.Interactivity;
using FileCrawler.Models;

namespace FileCrawler.Views
{
    /// <summary>Edits <see cref="AppSettings"/>. Closes with the updated settings on Save, or null on cancel/dismiss.</summary>
    public partial class SettingsDialog : Window
    {
        // The settings being edited, so Save preserves fields this dialog doesn't expose (drawer/sort state).
        private readonly AppSettings _settings = new();

        // Parameterless ctor for the XAML designer / previewer.
        public SettingsDialog() => InitializeComponent();

        public SettingsDialog(AppSettings settings) : this()
        {
            _settings = settings;
            MaxCrawlSecondsInput.Value = (decimal)settings.MaxCrawlSeconds;
        }

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            var seconds = (double)(MaxCrawlSecondsInput.Value ?? (decimal)AppSettings.DefaultMaxCrawlSeconds);
            Close(_settings with { MaxCrawlSeconds = seconds });
        }

        private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    }
}
