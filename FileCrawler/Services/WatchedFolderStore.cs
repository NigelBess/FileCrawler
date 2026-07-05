using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>The persisted shape of the watched-folder list (versioned for forward-compat).</summary>
/// <remarks><see cref="Blocked"/> was added in version 2; version-1 files load it as null (treated as empty).</remarks>
public sealed record WatchedFoldersFile(int Version, IReadOnlyList<string> Folders, IReadOnlyList<string>? Blocked);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WatchedFoldersFile))]
internal partial class WatchedFoldersJsonContext : JsonSerializerContext { }

/// <summary>
/// Stores the watched-folder list as JSON under %LOCALAPPDATA%\FileCrawler\watched-folders.json. Writes are
/// atomic (temp file + move) so a crash mid-write cannot corrupt the list; a missing or corrupt file loads empty.
/// </summary>
public sealed class WatchedFolderStore : IWatchedFolderStore
{
    private const int CurrentVersion = 2;
    private readonly string _dir;
    private readonly string _path;

    public WatchedFolderStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileCrawler");
        _path = Path.Combine(_dir, "watched-folders.json");
    }

    public async Task<WatchedFolderState> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return WatchedFolderState.Empty;
            await using var stream = File.OpenRead(_path);
            var data = await JsonSerializer.DeserializeAsync(stream, WatchedFoldersJsonContext.Default.WatchedFoldersFile)
                .ConfigureAwait(false);
            if (data is null) return WatchedFolderState.Empty;
            return new WatchedFolderState(
                data.Folders ?? Array.Empty<string>(),
                data.Blocked ?? Array.Empty<string>());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return WatchedFolderState.Empty;
        }
    }

    public async Task SaveAsync(IEnumerable<string> folders, IEnumerable<string> blocked)
    {
        Directory.CreateDirectory(_dir);
        var data = new WatchedFoldersFile(CurrentVersion, new List<string>(folders), new List<string>(blocked));
        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, data, WatchedFoldersJsonContext.Default.WatchedFoldersFile)
                .ConfigureAwait(false);
        }
        File.Move(tmp, _path, overwrite: true);
    }
}
