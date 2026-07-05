using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Material.Icons;
using Material.Icons.Avalonia;

namespace FileCrawler.Views
{
    public partial class BlockSubfolderDialog : Window
    {
        // Parameterless ctor for the XAML designer / previewer.
        public BlockSubfolderDialog() => InitializeComponent();

        /// <param name="candidatePaths">Folder levels, shallowest-first; one button is created per level.</param>
        public BlockSubfolderDialog(IReadOnlyList<string> candidatePaths) : this()
        {
            var indent = 0;
            foreach (var path in candidatePaths)
            {
                var name = Path.GetFileName(path) is { Length: > 0 } n ? n : path;
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    // Indent each deeper level so the hierarchy reads top-to-bottom.
                    Margin = new Avalonia.Thickness(indent, 0, 0, 0),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new MaterialIcon { Kind = MaterialIconKind.Folder, Width = 18, Height = 18 },
                            new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                button.Click += (_, _) => Close(path);
                LevelsPanel.Children.Add(button);
                indent += 16;
            }
        }

        private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    }
}
