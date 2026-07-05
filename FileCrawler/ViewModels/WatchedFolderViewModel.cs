using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCrawler.Models;
using FileCrawler.Utilities;

namespace FileCrawler.ViewModels;

/// <summary>A watched-folder row in the side panel.</summary>
public sealed partial class WatchedFolderViewModel : ObservableObject
{
    [ObservableProperty] private FileNode _root;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public WatchedFolderViewModel(string path, FileNode root)
    {
        Path = path;
        _root = root;
    }

    /// <summary>Normalized absolute path of the watched root (stable identity across recrawls).</summary>
    public string Path { get; }

    /// <summary>Subfolders under this root whose contents are excluded from crawling (shown nested, in red).</summary>
    public ObservableCollection<BlockedFolderViewModel> BlockedSubfolders { get; } = new();

    public string DisplayName => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;

    public string FormattedSize => SizeFormatter.Format(Root.SizeBytes);

    /// <summary>Raised so the view refreshes derived labels after a recrawl swaps the root.</summary>
    partial void OnRootChanged(FileNode value)
    {
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(DisplayName));
    }
}
