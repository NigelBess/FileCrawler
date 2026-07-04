using System;
using Avalonia.Media.Imaging;
using FileCrawler.Models;
using FileCrawler.Services;
using FileCrawler.Utilities;

namespace FileCrawler.ViewModels;

/// <summary>A single search result row. The full path is reconstructed once, when the row is created.</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(FileNode node)
    {
        Node = node;
        Name = node.Name;
        FullPath = node.FullPath;
        IsDirectory = node.IsDirectory;
        FormattedSize = SizeFormatter.Format(node.SizeBytes);
        Modified = node.ModifiedUtc == default
            ? ""
            : node.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Icon = FileIconProvider.GetIcon(node.Name, node.IsDirectory);
    }

    public FileNode Node { get; }
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string FormattedSize { get; }
    public string Modified { get; }

    /// <summary>Shell icon for the file type; null off-Windows or when extraction fails.</summary>
    public Bitmap? Icon { get; }

    /// <summary>Fallback glyph shown before the name when no shell icon is available.</summary>
    public string Kind => IsDirectory ? "📁" : "📄";
}
