using Avalonia.Controls;
using Avalonia.Interactivity;
using FileCrawler.Services;

namespace FileCrawler.Views
{
    /// <summary>
    /// Asks how to save the current filters. With no preset loaded it just prompts for a name (creating a new
    /// preset). With a preset loaded it offers to update that named preset or save as a new one. Closes with a
    /// <see cref="SavePresetResult"/> on Save, or null on cancel/dismiss.
    /// </summary>
    public partial class SavePresetDialog : Window
    {
        private readonly string? _currentPresetName;

        // Parameterless ctor for the XAML designer / previewer.
        public SavePresetDialog() => InitializeComponent();

        public SavePresetDialog(string? currentPresetName) : this()
        {
            _currentPresetName = currentPresetName;
            if (currentPresetName is not null)
            {
                ChoicePanel.IsVisible = true;
                UpdateOption.Content = $"Update “{currentPresetName}”";
                UpdateOption.IsCheckedChanged += (_, _) => SyncNameEnabled();
                NewOption.IsCheckedChanged += (_, _) => SyncNameEnabled();
                SyncNameEnabled();
            }
        }

        // The name box only matters when creating a new preset; updating reuses the current name.
        private void SyncNameEnabled() =>
            NamePanel.IsEnabled = _currentPresetName is null || NewOption.IsChecked == true;

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            if (_currentPresetName is not null && UpdateOption.IsChecked == true)
            {
                Close(new SavePresetResult(SavePresetAction.UpdateExisting, _currentPresetName));
                return;
            }

            var name = NameInput.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                // A new preset needs a name — keep the dialog open and focus the box.
                NameInput.Focus();
                return;
            }

            Close(new SavePresetResult(SavePresetAction.CreateNew, name));
        }

        private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    }
}
