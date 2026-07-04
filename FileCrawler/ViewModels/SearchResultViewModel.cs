using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCrawler.Models;
using FileCrawler.Services;
using FileCrawler.Utilities;

namespace FileCrawler.ViewModels;

/// <summary>A single search result row. The full path is reconstructed once, when the row is created.</summary>
public sealed class SearchResultViewModel : ObservableObject
{
    private readonly CancellationToken _lifetime;
    private Bitmap? _image;
    private bool _thumbnailRequested;

    /// <param name="lifetime">Cancelled when this result set is replaced, so a superseded row's
    /// still-queued thumbnail decode never runs.</param>
    public SearchResultViewModel(FileNode node, CancellationToken lifetime = default)
    {
        Node = node;
        Name = node.Name;
        FullPath = node.FullPath;
        IsDirectory = node.IsDirectory;
        FormattedSize = SizeFormatter.Format(node.SizeBytes);
        Modified = node.ModifiedUtc == default
            ? ""
            : node.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        _image = FileIconProvider.GetIcon(node.Name, node.IsDirectory);
        _lifetime = lifetime;
    }

    public FileNode Node { get; }
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string FormattedSize { get; }
    public string Modified { get; }

    /// <summary>
    /// Shell icon for the file type, upgraded to a decoded thumbnail for image files; null off-Windows
    /// or when extraction fails. Reading it starts the thumbnail load: the results list is virtualized,
    /// so only rows actually realized on screen are ever bound — offscreen results cost nothing, and
    /// scrolling drives loading naturally.
    /// </summary>
    public Bitmap? Image
    {
        get
        {
            BeginThumbnailLoad();
            return _image;
        }
        private set => SetProperty(ref _image, value);
    }

    /// <summary>Fallback glyph shown before the name when no shell icon is available.</summary>
    public string Kind => IsDirectory ? "📁" : "📄";

    private void BeginThumbnailLoad()
    {
        if (_thumbnailRequested || IsDirectory || !ThumbnailProvider.IsThumbnailable(Name))
            return;
        _thumbnailRequested = true;
        _ = LoadThumbnailAsync();
    }

    private async Task LoadThumbnailAsync()
    {
        try
        {
            var thumbnail = await ThumbnailProvider.GetThumbnailAsync(FullPath, Node.ModifiedUtc, _lifetime);
            if (thumbnail is not null && !_lifetime.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => Image = thumbnail);
        }
        catch (OperationCanceledException)
        {
            // Result set superseded before the decode ran; the shell icon stays.
        }
    }
}
