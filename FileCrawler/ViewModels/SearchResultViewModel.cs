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
    private Bitmap? _preview;
    private bool _thumbnailRequested;
    private bool _previewRequested;

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

    /// <summary>True for rows that get an enlarged mouse-over preview (decodable image files).</summary>
    public bool HasPreview => !IsDirectory && ThumbnailProvider.IsThumbnailable(Name);

    /// <summary>Row icon box size: image thumbnails fill the row height so the picture is legible;
    /// shell icons stay compact at their designed size.</summary>
    public double ImageSize => HasPreview ? 40 : 32;

    /// <summary>
    /// The enlarged hover preview. Read only when the tooltip actually opens, which is what starts the
    /// decode — hovering is the trigger, so previews cost nothing until used. Falls back to the row
    /// thumbnail while the sharper version is still decoding.
    /// </summary>
    public Bitmap? Preview
    {
        get
        {
            BeginPreviewLoad();
            return _preview ?? _image;
        }
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>Fallback glyph shown before the name when no shell icon is available.</summary>
    public string Kind => IsDirectory ? "📁" : "📄";

    private void BeginThumbnailLoad()
    {
        if (_thumbnailRequested || !HasPreview)
            return;
        _thumbnailRequested = true;
        _ = LoadAsync(ThumbnailProvider.GetThumbnailAsync, bitmap => Image = bitmap);
    }

    private void BeginPreviewLoad()
    {
        if (_previewRequested || !HasPreview)
            return;
        _previewRequested = true;
        _ = LoadAsync(ThumbnailProvider.GetPreviewAsync, bitmap => Preview = bitmap);
    }

    private async Task LoadAsync(
        Func<string, DateTime, CancellationToken, Task<Bitmap?>> load, Action<Bitmap> apply)
    {
        try
        {
            var bitmap = await load(FullPath, Node.ModifiedUtc, _lifetime);
            if (bitmap is not null && !_lifetime.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => apply(bitmap));
        }
        catch (OperationCanceledException)
        {
            // Result set superseded before the decode ran; the shell icon stays.
        }
    }
}
