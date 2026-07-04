using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace FileCrawler.Services;

/// <summary>Resolves Windows shell icons per file type. Returns null off-Windows or on failure.</summary>
public static class FileIconProvider
{
    private const string DirectoryKey = ":dir:";
    private const string NoExtensionKey = ":none:";

    // One bitmap per distinct extension for the process lifetime; shared across rows, never disposed.
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    /// <summary>Shell icon for the file type (the associated program's icon), or null so the caller
    /// can fall back to a glyph.</summary>
    public static Bitmap? GetIcon(string name, bool isDirectory)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var key = isDirectory ? DirectoryKey
            : Path.GetExtension(name) is { Length: > 0 } ext ? ext.ToLowerInvariant()
            : NoExtensionKey;
        // The guard is repeated inside the lambda because the platform analyzer can't see through it.
        return Cache.GetOrAdd(key, static k => OperatingSystem.IsWindows() ? TryLoad(k) : null);
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap? TryLoad(string key)
    {
        try
        {
            var isDirectory = key == DirectoryKey;
            var dummyName = key == DirectoryKey || key == NoExtensionKey ? "x" : "x" + key;
            var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            var shfi = default(SHFILEINFO);
            // SHGFI_USEFILEATTRIBUTES resolves the icon from the name/attributes alone — no disk access.
            var result = SHGetFileInfo(dummyName, attributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                return null;

            try
            {
                using var icon = System.Drawing.Icon.FromHandle(shfi.hIcon);
                using var gdiBitmap = icon.ToBitmap();
                using var stream = new MemoryStream();
                gdiBitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                return new Bitmap(stream);
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
        catch
        {
            // Odd extensions or a headless environment; caller shows the glyph instead.
            return null;
        }
    }

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi,
        uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
