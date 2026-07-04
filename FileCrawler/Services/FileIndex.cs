using System.Collections.Generic;
using System.Linq;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>
/// Holds crawled roots and republishes a combined immutable <see cref="AllNodes"/> snapshot on every mutation.
/// Mutations are serialized under a lock; the snapshot itself is published to a volatile field so the search
/// path reads a consistent list with no locking.
/// </summary>
public sealed class FileIndex : IFileIndex
{
    private readonly object _gate = new();
    private readonly List<CrawlResult> _results = new();

    private volatile IReadOnlyList<FileNode> _allNodes = System.Array.Empty<FileNode>();
    private volatile IReadOnlyList<FileNode> _roots = System.Array.Empty<FileNode>();

    public IReadOnlyList<FileNode> Roots => _roots;
    public IReadOnlyList<FileNode> AllNodes => _allNodes;

    public void AddRoot(CrawlResult result)
    {
        lock (_gate)
        {
            _results.Add(result);
            Rebuild();
        }
    }

    public void RemoveRoot(FileNode root)
    {
        lock (_gate)
        {
            _results.RemoveAll(r => ReferenceEquals(r.Root, root));
            Rebuild();
        }
    }

    public void ReplaceRoot(FileNode oldRoot, CrawlResult newResult)
    {
        lock (_gate)
        {
            _results.RemoveAll(r => ReferenceEquals(r.Root, oldRoot));
            _results.Add(newResult);
            Rebuild();
        }
    }

    // Caller holds _gate.
    private void Rebuild()
    {
        _roots = _results.Select(r => r.Root).ToArray();

        var total = 0;
        foreach (var r in _results) total += r.AllNodes.Count;
        var combined = new List<FileNode>(total);
        foreach (var r in _results) combined.AddRange(r.AllNodes);
        _allNodes = combined;
    }
}
