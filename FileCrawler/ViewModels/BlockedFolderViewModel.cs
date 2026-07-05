namespace FileCrawler.ViewModels;

/// <summary>A blocked subfolder row in the side panel: a folder whose contents are never included in results.</summary>
public sealed class BlockedFolderViewModel
{
    public BlockedFolderViewModel(string path) => Path = path;

    /// <summary>Normalized absolute path of the blocked subfolder.</summary>
    public string Path { get; }

    public string DisplayName => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
}
