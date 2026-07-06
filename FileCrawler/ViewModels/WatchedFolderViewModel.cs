using System;
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
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private TimeSpan _loadTime;

    /// <summary>True when the folder no longer exists on disk (moved/deleted). The row shows a warning and a
    /// delete button instead of the usual size/count stats.</summary>
    [ObservableProperty] private bool _isMissing;

    /// <summary>True when the last crawl hit the time limit before finishing; the row's contents are partial and
    /// the row shows a warning offering a one-click block of the largest subfolder.</summary>
    [ObservableProperty] private bool _isIncomplete;

    /// <summary>When <see cref="IsIncomplete"/>, the largest crawled subfolder to suggest blocking (its absolute
    /// path), or null when there's no candidate.</summary>
    [ObservableProperty] private string? _suggestedBlockPath;

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

    public string FormattedItemCount => $"{ItemCount:N0} files and subfolders";

    public string FormattedLoadTime => $"Loaded in {FormatDuration(LoadTime)}";

    /// <summary>The suggested block target's folder name (for display), or null when there's no suggestion.</summary>
    public string? SuggestedBlockName => SuggestedBlockPath is { Length: > 0 } path
        ? System.IO.Path.GetFileName(path) is { Length: > 0 } name ? name : path
        : null;

    /// <summary>Caption for the one-click block button on the incomplete-crawl warning.</summary>
    public string BlockSuggestionLabel =>
        SuggestedBlockName is { Length: > 0 } name ? $"Block “{name}”" : "Block largest subfolder";

    partial void OnSuggestedBlockPathChanged(string? value)
    {
        OnPropertyChanged(nameof(SuggestedBlockName));
        OnPropertyChanged(nameof(BlockSuggestionLabel));
    }

    /// <summary>Raised so the view refreshes derived labels after a recrawl swaps the root.</summary>
    partial void OnRootChanged(FileNode value)
    {
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnItemCountChanged(int value) => OnPropertyChanged(nameof(FormattedItemCount));

    partial void OnLoadTimeChanged(TimeSpan value) => OnPropertyChanged(nameof(FormattedLoadTime));

    /// <summary>Formats an elapsed crawl time with a natural unit (ms under a second, else seconds, else minutes).</summary>
    private static string FormatDuration(TimeSpan t) => t.TotalMilliseconds switch
    {
        < 1 => "<1 ms",
        < 1000 => $"{t.TotalMilliseconds:N0} ms",
        _ when t.TotalSeconds < 60 => $"{t.TotalSeconds:N1} s",
        _ => $"{t.TotalMinutes:N1} min",
    };
}
