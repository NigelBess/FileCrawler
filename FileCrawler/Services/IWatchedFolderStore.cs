using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>The persisted watched-folder configuration: the watched roots and the blocked subfolders.</summary>
public sealed record WatchedFolderState(IReadOnlyList<string> Folders, IReadOnlyList<string> Blocked)
{
    public static WatchedFolderState Empty { get; } =
        new(Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>Persists the watched-folder roots and blocked subfolders across runs.</summary>
public interface IWatchedFolderStore
{
    /// <summary>
    /// Loads the persisted workspace, or <c>null</c> if none has ever been saved (a true first run). A saved
    /// but empty workspace returns <see cref="WatchedFolderState.Empty"/>, not null, so an intentional
    /// "removed every folder" is not mistaken for first run.
    /// </summary>
    Task<WatchedFolderState?> LoadAsync();
    Task SaveAsync(IEnumerable<string> folders, IEnumerable<string> blocked);
}
