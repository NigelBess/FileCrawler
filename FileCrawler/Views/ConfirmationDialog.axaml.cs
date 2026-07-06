using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileCrawler.Views
{
    /// <summary>A modal yes/no confirmation. Closes with <c>true</c> on confirm, <c>false</c> on cancel/dismiss.</summary>
    public partial class ConfirmationDialog : Window
    {
        // Parameterless ctor for the XAML designer / previewer.
        public ConfirmationDialog() => InitializeComponent();

        public ConfirmationDialog(string title, string message, string confirmText, bool showCancel = true) : this()
        {
            Title = title;
            HeadingText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;
            // Informational (notify) dialogs have only an acknowledge button.
            CancelButton.IsVisible = showCancel;
        }

        private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

        private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    }
}
