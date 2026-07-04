using System;
using FileCrawler.Models;
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
    }

    public FileNode Node { get; }
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string FormattedSize { get; }
    public string Modified { get; }

    /// <summary>Glyph shown before the name (folder vs file).</summary>
    public string Kind => IsDirectory ? "📁" : "📄";
}
