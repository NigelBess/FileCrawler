using System.Collections.Generic;

namespace FileCrawler.Models;

/// <summary>A named group of file extensions (lowercase, with leading dot) shown as one filter checkbox.</summary>
public sealed record FileCategory(string Name, IReadOnlyList<string> Extensions);

/// <summary>The built-in file-type categories offered by the filter UI.</summary>
public static class FileCategories
{
    public static readonly IReadOnlyList<FileCategory> All =
    [
        new("Images",      [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff", ".heic", ".avif", ".raw"]),
        new("Documents",   [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".rtf", ".odt", ".ods", ".csv", ".epub"]),
        new("Videos",      [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg"]),
        new("Audio",       [".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus", ".mid"]),
        new("Archives",    [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso"]),
        new("Code",        [".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".css", ".html", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh", ".ps1", ".axaml", ".xaml"]),
        new("Executables", [".exe", ".msi", ".dll", ".bat", ".cmd", ".appx", ".apk"]),
    ];
}
