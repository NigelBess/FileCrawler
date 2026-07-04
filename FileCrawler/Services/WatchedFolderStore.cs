using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>The persisted shape of the watched-folder list (versioned for forward-compat).</summary>
public sealed record WatchedFoldersFile(int Version, IReadOnlyList<string> Folders);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WatchedFoldersFile))]
internal partial class WatchedFoldersJsonContext : JsonSerializerContext { }

/// <summary>
/// Stores the watched-folder list as JSON under %LOCALAPPDATA%\FileCrawler\watched-folders.json. Writes are
/// atomic (temp file + move) so a crash mid-write cannot corrupt the list; a missing or corrupt file loads empty.
/// </summary>
public sealed class WatchedFolderStore : IWatchedFolderStore
{
    private const int CurrentVersion = 1;
    private readonly string _dir;
    private readonly string _path;

    public WatchedFolderStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileCrawler");
        _path = Path.Combine(_dir, "watched-folders.json");
    }

    public async Task<IReadOnlyList<string>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<string>();
            await using var stream = File.OpenRead(_path);
            var data = await JsonSerializer.DeserializeAsync(stream, WatchedFoldersJsonContext.Default.WatchedFoldersFile)
                .ConfigureAwait(false);
            return data?.Folders ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public async Task SaveAsync(IEnumerable<string> folders)
    {
        Directory.CreateDirectory(_dir);
        var data = new WatchedFoldersFile(CurrentVersion, new List<string>(folders));
        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, data, WatchedFoldersJsonContext.Default.WatchedFoldersFile)
                .ConfigureAwait(false);
        }
        File.Move(tmp, _path, overwrite: true);
    }
}
