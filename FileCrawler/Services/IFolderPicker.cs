using System.Threading.Tasks;

namespace FileCrawler.Services;

/// <summary>Abstracts the platform folder-picker so view models stay view-agnostic and testable.</summary>
public interface IFolderPicker
{
    /// <summary>Prompts the user to pick a folder; returns its path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync();
}
