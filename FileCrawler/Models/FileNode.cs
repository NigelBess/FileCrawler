using System;
using System.IO;

namespace FileCrawler.Models;

/// <summary>
/// A single file or directory in an in-memory crawl tree.
/// </summary>
/// <remarks>
/// Only the <see cref="Name"/> is stored per node; the full path is reconstructed lazily by walking
/// <see cref="Parent"/>. Full paths are hugely redundant across siblings and would dominate memory in a tree
/// of millions of nodes, so we pay a small walk cost only for the handful of rows actually shown to the user.
/// </remarks>
public sealed class FileNode
{
    /// <summary>The file or directory name only (no path).</summary>
    public required string Name { get; init; }

    /// <summary>For a file, its own size. For a directory, the recursive net size of all nested content.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Last write time (UTC).</summary>
    public DateTime ModifiedUtc { get; init; }

    /// <summary>True when this node is a directory.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>The parent node, or null for a watched root.</summary>
    public FileNode? Parent { get; set; }

    /// <summary>Child nodes for a directory; null for files (and empty/inaccessible directories may be an empty array).</summary>
    public FileNode[]? Children { get; set; }

    /// <summary>The full filesystem path, reconstructed by walking up the parent chain.</summary>
    public string FullPath => Parent is null ? Name : Path.Combine(Parent.FullPath, Name);
}
