using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCrawler.ViewModels;

namespace FileCrawler.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        private void OnAddBlockedFolder(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is WatchedFolderViewModel folder)
                ViewModel?.AddBlockedSubfolderCommand.Execute(folder);
        }

        private void OnRemoveFolder(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is WatchedFolderViewModel folder)
                ViewModel?.RemoveFolderCommand.Execute(folder);
        }

        private void OnBlockSuggestedSubfolder(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is WatchedFolderViewModel folder)
                ViewModel?.BlockSuggestedSubfolderCommand.Execute(folder);
        }

        private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
        {
            // Resolve the row from the actually-tapped element rather than SelectedItem, which may not have
            // updated by the time the double-tap fires.
            var result = (e.Source as Visual)?
                .GetSelfAndVisualAncestors()
                .OfType<Control>()
                .Select(c => c.DataContext)
                .OfType<SearchResultViewModel>()
                .FirstOrDefault()
                ?? ResultsList.SelectedItem as SearchResultViewModel;

            if (result is not null)
                ViewModel?.OpenResultCommand.Execute(result);
        }

        private void OnOpenResult(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SearchResultViewModel result)
                ViewModel?.OpenResultCommand.Execute(result);
        }

        private void OnRevealResult(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SearchResultViewModel result)
                ViewModel?.RevealResultCommand.Execute(result);
        }

        private void OnBlockSubfolderThisSearch(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SearchResultViewModel result)
                ViewModel?.BlockSubfolderFromThisSearchCommand.Execute(result);
        }

        private void OnBlockSubfolderAllSearches(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SearchResultViewModel result)
                ViewModel?.BlockSubfolderFromAllSearchesCommand.Execute(result);
        }

        private void OnUnblockFolder(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is BlockedFolderViewModel blocked)
                ViewModel?.UnblockSubfolderCommand.Execute(blocked);
        }

        private void OnRemoveSearchBlock(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is BlockedFolderViewModel blocked)
                ViewModel?.Filters.RemoveSearchBlockCommand.Execute(blocked);
        }
    }
}
