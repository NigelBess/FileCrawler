using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using FileCrawler.Views;

namespace FileCrawler.Services;

/// <summary>What the user chose to do in the save-preset dialog.</summary>
public enum SavePresetAction { CreateNew, UpdateExisting }

/// <summary>The outcome of the save-preset dialog: the chosen action and the target preset name.</summary>
public sealed record SavePresetResult(SavePresetAction Action, string Name);

/// <summary>
/// Prompts the user to save the current filters as a preset — either creating a new one (asking for its name)
/// or, when a preset is already loaded, updating it. Returns null if the user cancels. Abstracted so the view
/// model stays view-agnostic and testable.
/// </summary>
public interface IPresetSavePrompt
{
    /// <param name="currentPresetName">The loaded preset's name, or null when none is loaded (create-only).</param>
    Task<SavePresetResult?> PromptAsync(string? currentPresetName);
}

/// <summary>Shows a modal <see cref="SavePresetDialog"/> owned by a supplied window (the main window).</summary>
public sealed class DialogPresetSavePrompt : IPresetSavePrompt
{
    private readonly Func<Window?> _owner;

    public DialogPresetSavePrompt(Func<Window?> owner) => _owner = owner;

    public async Task<SavePresetResult?> PromptAsync(string? currentPresetName)
    {
        var owner = _owner();
        if (owner is null) return null;

        var dialog = new SavePresetDialog(currentPresetName);
        return await dialog.ShowDialog<SavePresetResult?>(owner);
    }
}
