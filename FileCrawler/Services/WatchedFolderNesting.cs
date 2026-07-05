using System;
using System.Collections.Generic;
using System.IO;

namespace FileCrawler.Services;

/// <summary>
/// Pure helpers for normalizing watched-folder paths and resolving nesting so that no watched root is ever
/// contained inside another.
/// </summary>
public static class WatchedFolderNesting
{
    /// <summary>Returns an absolute path with any trailing directory separator removed.</summary>
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>
    /// True when <paramref name="child"/> is equal to or nested under <paramref name="ancestor"/>.
    /// Compares with a trailing separator so "C:\foo" does not falsely contain "C:\foobar".
    /// </summary>
    public static bool IsSameOrDescendant(string child, string ancestor)
    {
        if (string.Equals(child, ancestor, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = ancestor + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns each folder level between <paramref name="owner"/> (exclusive) and <paramref name="target"/>
    /// (inclusive), ordered shallowest-first. These are the candidate folders a user can choose to block for a
    /// result found under <paramref name="owner"/>. Empty when <paramref name="target"/> is the owner itself or
    /// not nested under it. All inputs and outputs are normalized.
    /// </summary>
    public static IReadOnlyList<string> LevelsBetween(string target, string owner)
    {
        var levels = new List<string>();
        var current = target;
        while (IsSameOrDescendant(current, owner) &&
               !string.Equals(current, owner, StringComparison.OrdinalIgnoreCase))
        {
            levels.Add(current);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)) break;
            current = Normalize(parent);
        }
        levels.Reverse();
        return levels;
    }

    /// <summary>The outcome of resolving a candidate folder against the existing watched roots.</summary>
    /// <param name="CanAdd">Whether the candidate should be added.</param>
    /// <param name="CoveredBy">If it cannot be added, the existing root that already covers it.</param>
    /// <param name="Superseded">Existing roots that are nested inside the candidate and should be removed.</param>
    public sealed record Resolution(bool CanAdd, string? CoveredBy, IReadOnlyList<string> Superseded);

    /// <summary>
    /// Resolves adding <paramref name="candidate"/> against <paramref name="existing"/> (all assumed normalized
    /// and mutually non-nested). If the candidate is equal to or inside an existing root it cannot be added;
    /// otherwise any existing roots nested inside the candidate are superseded.
    /// </summary>
    public static Resolution Resolve(string candidate, IEnumerable<string> existing)
    {
        var superseded = new List<string>();
        foreach (var e in existing)
        {
            if (IsSameOrDescendant(candidate, e))
                return new Resolution(false, e, Array.Empty<string>());
            if (IsSameOrDescendant(e, candidate))
                superseded.Add(e);
        }
        return new Resolution(true, null, superseded);
    }
}
