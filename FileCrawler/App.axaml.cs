using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileCrawler.Services;
using FileCrawler.ViewModels;
using FileCrawler.Views;

namespace FileCrawler
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();

                // Composition root: construct services once and inject into the main view model.
                var crawler = new DirectoryCrawler();
                var index = new FileIndex();
                var store = new WatchedFolderStore();
                var searchStateStore = new SearchStateStore();
                var search = new SearchService(index);
                var picker = new StorageFolderPicker(() => window);
                var blockPicker = new DialogSubfolderBlockPicker(() => window);
                var confirm = new DialogConfirmationService(() => window);

                var viewModel = new MainWindowViewModel(
                    crawler, index, store, searchStateStore, search, picker, blockPicker, confirm);
                window.DataContext = viewModel;
                desktop.MainWindow = window;

                // Load persisted folders and crawl them once the window is up.
                window.Opened += async (_, _) => await viewModel.InitializeAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
