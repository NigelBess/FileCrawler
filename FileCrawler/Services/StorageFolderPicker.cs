using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace FileCrawler.Services;

/// <summary>
/// Folder picker backed by Avalonia's <see cref="IStorageProvider"/>, resolved from a supplied top-level
/// (the main window).
/// </summary>
public sealed class StorageFolderPicker : IFolderPicker
{
    private readonly Func<TopLevel?> _topLevel;

    public StorageFolderPicker(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> PickFolderAsync()
    {
        var top = _topLevel();
        if (top is null) return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder to watch",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        return folder?.TryGetLocalPath();
    }
}
