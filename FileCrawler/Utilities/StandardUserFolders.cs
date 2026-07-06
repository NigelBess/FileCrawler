using System;
using System.Collections.Generic;
using System.IO;
using FileCrawler.Services;

namespace FileCrawler.Utilities;

/// <summary>
/// Resolves the current user's standard content folders — Desktop, Documents, Downloads, Music, Pictures and
/// Videos — in a cross-platform way (Windows, macOS, Linux). Used to seed the watched-folder list on first run.
/// </summary>
public static class StandardUserFolders
{
    /// <summary>
    /// The standard per-user content folders that actually exist on this machine, normalized and de-nested so
    /// no folder is contained within another. Folders that don't exist — or that resolve to the home directory
    /// itself, as some can on Linux when the XDG dirs are unconfigured — are skipped so we never seed the whole
    /// home folder.
    /// </summary>
    public static IReadOnlyList<string> Resolve()
    {
        var home = SafeGetFolder(Environment.SpecialFolder.UserProfile);
        var normalizedHome = home.Length == 0 ? null : WatchedFolderNesting.Normalize(home);

        var candidates = new[]
        {
            SafeGetFolder(Environment.SpecialFolder.DesktopDirectory),
            SafeGetFolder(Environment.SpecialFolder.MyDocuments),
            SafeGetFolder(Environment.SpecialFolder.MyMusic),
            SafeGetFolder(Environment.SpecialFolder.MyPictures),
            SafeGetFolder(Environment.SpecialFolder.MyVideos),
            // Downloads has no SpecialFolder enum; ~/Downloads is the convention on every desktop OS.
            home.Length == 0 ? "" : Path.Combine(home, "Downloads"),
        };

        var result = new List<string>();
        foreach (var raw in candidates)
        {
            if (string.IsNullOrEmpty(raw) || !Directory.Exists(raw)) continue;

            var normalized = WatchedFolderNesting.Normalize(raw);
            if (normalizedHome is not null &&
                string.Equals(normalized, normalizedHome, StringComparison.OrdinalIgnoreCase))
                continue;

            // Fold the candidate into the running set the same way the UI does, so a duplicate or a
            // parent/child pair (e.g. Desktop resolving equal to DesktopDirectory) never yields nested roots.
            var resolution = WatchedFolderNesting.Resolve(normalized, result);
            if (!resolution.CanAdd) continue;
            foreach (var superseded in resolution.Superseded) result.Remove(superseded);
            result.Add(normalized);
        }

        return result;
    }

    private static string SafeGetFolder(Environment.SpecialFolder folder)
    {
        try { return Environment.GetFolderPath(folder); }
        catch { return ""; }
    }
}
