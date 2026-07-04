using System.Diagnostics;

namespace FileCrawler.Services;

/// <summary>Opens files/folders and reveals them in Windows Explorer.</summary>
public static class FileLauncher
{
    /// <summary>Opens a file or folder with its default handler.</summary>
    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Path may have moved since the crawl; swallow rather than crash the UI.
        }
    }

    /// <summary>Opens Explorer with the item selected (or the folder itself for a directory).</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}
