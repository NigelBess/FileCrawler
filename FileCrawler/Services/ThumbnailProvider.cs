using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace FileCrawler.Services;

/// <summary>
/// Decodes small image thumbnails (and larger hover previews) in the background, bounded in both compute
/// and memory. Rows render instantly with their shell icon; a thumbnail swaps in when decoding finishes.
/// At most two decodes run concurrently (typing fast can queue many rows; the gate keeps CPU/disk flat),
/// queued work exits at the gate when its result set is superseded, and decoded bitmaps live in a
/// byte-budgeted LRU cache so refining a query re-shows earlier thumbnails for free.
/// </summary>
public static class ThumbnailProvider
{
    /// <summary>Decode width in pixels — covers the 40px row slot at 2× DPI. ~25 KB decoded per entry.</summary>
    public const int DecodeWidth = 80;

    /// <summary>Decode width for the enlarged mouse-over preview. ~256 KB decoded per entry.</summary>
    public const int PreviewWidth = 256;

    /// <summary>Total decoded-pixel budget. Holds a full 1000-row result set of thumbnails (~16 MB)
    /// plus a few dozen hover previews before the least-recently-used entries are evicted.</summary>
    private const long CacheBudgetBytes = 32 * 1024 * 1024;

    /// <summary>Nominal cost for a cached decode failure, so bad files are remembered but bounded.</summary>
    private const long FailureCostBytes = 1024;

    /// <summary>Formats Skia decodes reliably; other image types keep their shell icon.</summary>
    private static readonly HashSet<string> DecodableExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private static readonly SemaphoreSlim DecodeGate = new(2);

    // LRU cache: most-recently-used at the front, evicted from the tail once over budget. Failed decodes
    // cache null so a bad file is tried once. Evicted bitmaps are not disposed — a visible row may still
    // be bound to one; the GC reclaims them.
    private static readonly object CacheLock = new();
    private static readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> Cache = new();
    private static readonly LinkedList<CacheEntry> Recency = new();
    private static long _cachedBytes;

    // Coalesces concurrent requests for the same file (e.g. a row rebinding during scroll).
    private static readonly ConcurrentDictionary<CacheKey, Task<Bitmap?>> InFlight = new();

    /// <summary>True when the file type is one this provider can decode.</summary>
    public static bool IsThumbnailable(string name) =>
        Path.GetExtension(name) is { Length: > 0 } ext && DecodableExtensions.Contains(ext);

    /// <summary>
    /// The row thumbnail for a file, decoding it if not cached; null when the file can't be decoded.
    /// The modified time keys the cache, so a re-crawled, changed file gets a fresh thumbnail.
    /// </summary>
    public static Task<Bitmap?> GetThumbnailAsync(string fullPath, DateTime modifiedUtc, CancellationToken ct) =>
        GetAsync(new CacheKey(fullPath, modifiedUtc, DecodeWidth), ct);

    /// <summary>The enlarged mouse-over preview for a file; same contract as <see cref="GetThumbnailAsync"/>.</summary>
    public static Task<Bitmap?> GetPreviewAsync(string fullPath, DateTime modifiedUtc, CancellationToken ct) =>
        GetAsync(new CacheKey(fullPath, modifiedUtc, PreviewWidth), ct);

    private static Task<Bitmap?> GetAsync(CacheKey key, CancellationToken ct)
    {
        if (TryGetCached(key, out var cached))
            return Task.FromResult(cached);

        var task = InFlight.GetOrAdd(key, k => Task.Run(() => LoadAsync(k, ct), ct));
        task.ContinueWith(_ => InFlight.TryRemove(key, out var _), TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    private static async Task<Bitmap?> LoadAsync(CacheKey key, CancellationToken ct)
    {
        await DecodeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (TryGetCached(key, out var cached))
                return cached;

            Bitmap? bitmap;
            try
            {
                using var stream = new FileStream(key.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                bitmap = Bitmap.DecodeToWidth(stream, key.Width);
            }
            catch
            {
                // Unreadable or corrupt file: remember the failure so the row keeps its shell icon.
                bitmap = null;
            }

            AddToCache(key, bitmap);
            return bitmap;
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static bool TryGetCached(CacheKey key, out Bitmap? bitmap)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var node))
            {
                Recency.Remove(node);
                Recency.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }
        bitmap = null;
        return false;
    }

    private static void AddToCache(CacheKey key, Bitmap? bitmap)
    {
        var cost = bitmap is null
            ? FailureCostBytes
            : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

        lock (CacheLock)
        {
            if (Cache.ContainsKey(key))
                return;

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, bitmap, cost));
            Cache[key] = node;
            Recency.AddFirst(node);
            _cachedBytes += cost;

            while (_cachedBytes > CacheBudgetBytes && Recency.Count > 1)
            {
                var oldest = Recency.Last!;
                Recency.RemoveLast();
                Cache.Remove(oldest.Value.Key);
                _cachedBytes -= oldest.Value.CostBytes;
            }
        }
    }

    private readonly record struct CacheKey(string Path, DateTime ModifiedUtc, int Width);
    private readonly record struct CacheEntry(CacheKey Key, Bitmap? Bitmap, long CostBytes);
}
