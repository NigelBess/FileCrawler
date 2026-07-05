using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileCrawler.Views
{
    /// <summary>
    /// A reusable red-bordered chip for one blocked folder (icon + name + an X to remove it). Its DataContext is a
    /// <see cref="ViewModels.BlockedFolderViewModel"/>. Clicking the X raises <see cref="RemoveRequested"/>, which
    /// bubbles so the host (search filters, watched-folders sidebar) can route it to the appropriate command.
    /// </summary>
    public partial class BlockedFolderChip : UserControl
    {
        public static readonly RoutedEvent<RoutedEventArgs> RemoveRequestedEvent =
            RoutedEvent.Register<BlockedFolderChip, RoutedEventArgs>(
                nameof(RemoveRequested), RoutingStrategies.Bubble);

        /// <summary>Raised when the user clicks the chip's X. Handle it on the chip element in XAML.</summary>
        public event EventHandler<RoutedEventArgs> RemoveRequested
        {
            add => AddHandler(RemoveRequestedEvent, value);
            remove => RemoveHandler(RemoveRequestedEvent, value);
        }

        public BlockedFolderChip() => InitializeComponent();

        // Raise from the chip itself so handlers can read its DataContext (the BlockedFolderViewModel).
        private void OnRemoveClick(object? sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(RemoveRequestedEvent, this));
    }
}
