using System.Collections.Generic;
using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>
/// Prompts the user to choose which folder level to block from a set of candidate folders (one per level of
/// the hierarchy between a watched root and a result). Abstracted so view models stay view-agnostic and testable.
/// </summary>
public interface ISubfolderBlockPicker
{
    /// <summary>
    /// Shows <paramref name="candidatePaths"/> (shallowest-first) as choices and returns the chosen folder
    /// path, or null if cancelled.
    /// </summary>
    Task<string?> PickAsync(IReadOnlyList<string> candidatePaths);
}
