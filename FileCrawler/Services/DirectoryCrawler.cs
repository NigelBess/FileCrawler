using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using FileCrawler.Models;

namespace FileCrawler.Services;

/// <summary>
/// Fast directory crawler built on <see cref="FileSystemEnumerable{T}"/>, which surfaces name, size and
/// last-write-time straight from the OS enumeration stream with no extra per-file syscalls. Subtrees under the
/// root are crawled in parallel; folder sizes are aggregated post-order.
/// </summary>
public sealed class DirectoryCrawler : IDirectoryCrawler
{
    // Recurse one level at a time so we can build the tree and fan out across subtrees ourselves.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0, // include hidden/system entries
    };

    /// <summary>Lightweight projection of a directory entry; avoids per-file FileInfo allocations.</summary>
    private readonly record struct Entry(string Name, long Size, DateTime ModifiedUtc, bool IsDirectory, bool IsReparse);

    public async Task<CrawlResult> CrawlAsync(string rootPath, IProgress<CrawlProgress>? progress, CancellationToken ct)
    {
        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        DateTime rootModified;
        try { rootModified = Directory.GetLastWriteTimeUtc(rootPath); }
        catch { rootModified = default; }

        var skipped = 0;
        var nodeCount = 0;

        // The root node's Name is the full absolute path so that FullPath (walked from any descendant up
        // to the root) resolves to an absolute path — required for opening/revealing results.
        var root = new FileNode
        {
            Name = rootPath,
            IsDirectory = true,
            ModifiedUtc = rootModified,
        };

        await Task.Run(() => CrawlDirectory(root, rootPath, rootPath, progress, ref skipped, ref nodeCount, parallelize: true, ct), ct)
            .ConfigureAwait(false);

        var all = Flatten(root);
        return new CrawlResult(root, all, skipped);
    }

    /// <summary>
    /// Populates <paramref name="dir"/>'s children and aggregated size by enumerating <paramref name="dirPath"/>.
    /// Immediate subdirectories are crawled in parallel only when <paramref name="parallelize"/> is set
    /// (used at the root) to spread top-level subtrees across cores without unbounded task fan-out.
    /// </summary>
    private void CrawlDirectory(
        FileNode dir,
        string dirPath,
        string rootPath,
        IProgress<CrawlProgress>? progress,
        ref int skipped,
        ref int nodeCount,
        bool parallelize,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var children = new List<FileNode>();
        var subDirs = new List<FileNode>(); // directory children needing recursion (non-reparse)
        long ownSize = 0;

        try
        {
            var enumerable = new FileSystemEnumerable<Entry>(
                dirPath,
                (ref FileSystemEntry e) => new Entry(
                    e.FileName.ToString(),
                    e.IsDirectory ? 0 : e.Length,
                    e.LastWriteTimeUtc.UtcDateTime,
                    e.IsDirectory,
                    (e.Attributes & FileAttributes.ReparsePoint) != 0),
                Options);

            foreach (var entry in enumerable)
            {
                var node = new FileNode
                {
                    Name = entry.Name,
                    IsDirectory = entry.IsDirectory,
                    ModifiedUtc = entry.ModifiedUtc,
                    Parent = dir,
                    SizeBytes = entry.Size,
                };

                if (entry.IsDirectory)
                {
                    node.Children = Array.Empty<FileNode>();
                    // Reparse points (junctions/symlinks) are listed but never recursed into: doing so
                    // risks cycles and double-counted sizes.
                    if (!entry.IsReparse) subDirs.Add(node);
                }
                else
                {
                    ownSize += entry.Size;
                }

                children.Add(node);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            // Record and skip this directory; never abort the whole crawl over one inaccessible folder.
            Interlocked.Increment(ref skipped);
        }

        var count = Interlocked.Add(ref nodeCount, children.Count);
        progress?.Report(new CrawlProgress(rootPath, count));

        // Recurse into subdirectories (post-order size aggregation).
        long childrenSize = 0;
        if (subDirs.Count > 0)
        {
            if (parallelize)
            {
                var partials = new long[subDirs.Count];
                var localSkipped = 0;
                var localCount = 0;
                Parallel.For(0, subDirs.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
                    i =>
                    {
                        var sub = subDirs[i];
                        CrawlDirectory(sub, Path.Combine(dirPath, sub.Name), rootPath, progress,
                            ref localSkipped, ref localCount, parallelize: false, ct);
                        partials[i] = sub.SizeBytes;
                    });
                Interlocked.Add(ref skipped, localSkipped);
                Interlocked.Add(ref nodeCount, localCount);
                foreach (var p in partials) childrenSize += p;
            }
            else
            {
                foreach (var sub in subDirs)
                {
                    CrawlDirectory(sub, Path.Combine(dirPath, sub.Name), rootPath, progress,
                        ref skipped, ref nodeCount, parallelize: false, ct);
                    childrenSize += sub.SizeBytes;
                }
            }
        }

        dir.Children = children.ToArray();
        dir.SizeBytes = ownSize + childrenSize;
    }

    /// <summary>Depth-first flatten of the tree into a single list (root included), for the search index.</summary>
    private static IReadOnlyList<FileNode> Flatten(FileNode root)
    {
        var list = new List<FileNode>();
        var stack = new Stack<FileNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            list.Add(node);
            if (node.Children is { Length: > 0 } children)
            {
                foreach (var c in children) stack.Push(c);
            }
        }
        return list;
    }
}
