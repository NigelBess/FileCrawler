using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>
/// Persists the watched-folder roots and blocked subfolders via <see cref="UserPersistentData"/>
/// (stored under %LOCALAPPDATA%\FileCrawler\workspace.json).
/// </summary>
/// <remarks>
/// The previous release stored this data as a standalone %LOCALAPPDATA%\FileCrawler\watched-folders.json
/// file; that file is transparently migrated into the new store on first load so existing workspaces survive.
/// </remarks>
public sealed class WatchedFolderStore : IWatchedFolderStore
{
    private const string FoldersKey = "watchedFolders";
    private const string BlockedKey = "blockedSubfolders";

    private readonly UserPersistentData _data = new("workspace");
    private readonly string _legacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FileCrawler", "watched-folders.json");

    /// <summary>The legacy standalone file shape (version 1 had no <see cref="Blocked"/>).</summary>
    private sealed record LegacyFile(int Version, IReadOnlyList<string>? Folders, IReadOnlyList<string>? Blocked);

    public Task<WatchedFolderState?> LoadAsync() => Task.Run(Load);

    public Task SaveAsync(IEnumerable<string> folders, IEnumerable<string> blocked) =>
        Task.Run(() => Save(folders, blocked));

    private WatchedFolderState? Load()
    {
        var hasFolders = _data.TryLoad<List<string>>(FoldersKey, out var folders);
        var hasBlocked = _data.TryLoad<List<string>>(BlockedKey, out var blocked);
        if (hasFolders || hasBlocked)
            return new WatchedFolderState(folders ?? new(), blocked ?? new());

        // Nothing in the store: a migrated legacy file counts as configured; otherwise this is a first run.
        return TryMigrateLegacy();
    }

    private void Save(IEnumerable<string> folders, IEnumerable<string> blocked)
    {
        _data.Save(FoldersKey, folders.ToList());
        _data.Save(BlockedKey, blocked.ToList());
    }

    /// <summary>Imports the pre-existing standalone file into the new store, then retires it. Null if there is none.</summary>
    private WatchedFolderState? TryMigrateLegacy()
    {
        try
        {
            if (!File.Exists(_legacyPath)) return null;
            var legacy = JsonSerializer.Deserialize<LegacyFile>(File.ReadAllText(_legacyPath));
            if (legacy is null) return null;

            var state = new WatchedFolderState(
                legacy.Folders ?? Array.Empty<string>(),
                legacy.Blocked ?? Array.Empty<string>());
            Save(state.Folders, state.Blocked);

            try { File.Move(_legacyPath, _legacyPath + ".migrated", overwrite: true); }
            catch (IOException) { /* migration already succeeded; leaving the old file is harmless */ }

            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
