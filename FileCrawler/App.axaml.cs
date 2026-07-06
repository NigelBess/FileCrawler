using System.Threading.Tasks;
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

                // When running from the source checkout, check the remote for a newer build and offer to self-update.
                var updateService = new GitUpdateService();
                window.Opened += async (_, _) => await CheckForUpdatesAsync(updateService, confirm, desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Fetches from the remote and, if the source checkout is behind, offers to pull + rebuild + relaunch.
        /// Runs fire-and-forget after the window opens; failures are swallowed so a check never disrupts startup.
        /// </summary>
        private static async Task CheckForUpdatesAsync(
            IUpdateService updateService, IConfirmationService confirm, IClassicDesktopStyleApplicationLifetime desktop)
        {
            var status = await updateService.CheckAsync();
            switch (status.State)
            {
                case UpdateAvailability.Available:
                    var commits = status.CommitsBehind == 1 ? "1 new commit" : $"{status.CommitsBehind} new commits";
                    var confirmed = await confirm.ConfirmAsync(
                        "Update available",
                        $"A newer version is available ({commits}).\n\n" +
                        "Updating will pull the latest code, rebuild, and relaunch the app. Continue?",
                        "Update");
                    if (confirmed && updateService.LaunchUpdater())
                        desktop.Shutdown();
                    break;

                case UpdateAvailability.AvailableButDirty:
                    await confirm.NotifyAsync(
                        "Update available",
                        "A newer version is available, but you have uncommitted changes in this checkout, so the " +
                        "update was skipped.\n\n" +
                        "Commit your work or stash it (git stash), then restart the app to update.");
                    break;
            }
        }
    }
}
