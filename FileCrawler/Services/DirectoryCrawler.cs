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
    /// <summary>
    /// Upper bound on how many items we count beneath blocked folders. Blocking exists to avoid indexing
    /// large trees, so we never fully enumerate one — counting stops here, and the UI reports "over N".
    /// </summary>
    public const int BlockedCountCap = 1000;

    // Recurse one level at a time so we can build the tree and fan out across subtrees ourselves.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0, // include hidden/system entries
    };

    // Blocked-subtree counting only needs a running tally, so recurse in one pass with no per-level fan-out.
    private static readonly EnumerationOptions CountOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    private static readonly IReadOnlySet<string> NoBlocked = new HashSet<string>();

    /// <summary>Lightweight projection of a directory entry; avoids per-file FileInfo allocations.</summary>
    private readonly record struct Entry(string Name, long Size, DateTime ModifiedUtc, bool IsDirectory, bool IsReparse);

    public async Task<CrawlResult> CrawlAsync(
        string rootPath,
        IReadOnlySet<string>? blockedFolders,
        IProgress<CrawlProgress>? progress,
        CancellationToken ct)
    {
        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var blocked = blockedFolders ?? NoBlocked;

        var exists = Directory.Exists(rootPath);

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

        await Task.Run(() => CrawlDirectory(root, rootPath, rootPath, blocked, progress, ref skipped, ref nodeCount, parallelize: true, ct), ct)
            .ConfigureAwait(false);

        var all = Flatten(root);
        var (blockedItems, blockedCapped) = CountBlocked(blocked, ct);
        return new CrawlResult(root, all, skipped, blockedItems, blockedCapped, exists);
    }

    /// <summary>
    /// Counts files and subfolders beneath the blocked folders, stopping as soon as the running total reaches
    /// <see cref="BlockedCountCap"/> so a huge blocked tree (e.g. node_modules) costs only a bounded scan.
    /// Returns the tally and whether it was truncated at the cap (making the count a lower bound). Best-effort:
    /// inaccessible or cancelled scans just contribute what they saw and never fail the crawl.
    /// </summary>
    private static (int Count, bool Capped) CountBlocked(IReadOnlySet<string> blocked, CancellationToken ct)
    {
        if (blocked.Count == 0) return (0, false);

        var count = 0;
        foreach (var folder in blocked)
        {
            if (ct.IsCancellationRequested) return (count, false);
            try
            {
                // Project to a throwaway byte — we only care about the count of entries, not their data.
                var entries = new FileSystemEnumerable<byte>(folder, static (ref FileSystemEntry _) => 0, CountOptions);
                foreach (var _ in entries)
                {
                    if (++count >= BlockedCountCap) return (BlockedCountCap, true);
                    if (ct.IsCancellationRequested) return (count, false);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                // Blocked folder gone or unreadable; skip it and keep the partial tally.
            }
        }

        return (count, false);
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
        IReadOnlySet<string> blocked,
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
                // Blocked subfolders are excluded entirely: skip the directory and its whole subtree so
                // neither it nor its contents ever reach the search index or size totals.
                if (entry.IsDirectory && blocked.Count > 0 && blocked.Contains(Path.Combine(dirPath, entry.Name)))
                    continue;

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
                        CrawlDirectory(sub, Path.Combine(dirPath, sub.Name), rootPath, blocked, progress,
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
                    CrawlDirectory(sub, Path.Combine(dirPath, sub.Name), rootPath, blocked, progress,
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
