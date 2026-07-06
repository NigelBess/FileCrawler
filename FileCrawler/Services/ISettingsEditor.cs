using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using FileCrawler.Models;
using FileCrawler.Views;

namespace FileCrawler.Services;

/// <summary>
/// Shows the settings editor and returns the updated settings, or null if the user cancels. Abstracted so the
/// view model stays view-agnostic and testable.
/// </summary>
public interface ISettingsEditor
{
    Task<AppSettings?> EditAsync(AppSettings current);
}

/// <summary>Shows a modal <see cref="SettingsDialog"/> owned by a supplied window (the main window).</summary>
public sealed class DialogSettingsEditor : ISettingsEditor
{
    private readonly Func<Window?> _owner;

    public DialogSettingsEditor(Func<Window?> owner) => _owner = owner;

    public async Task<AppSettings?> EditAsync(AppSettings current)
    {
        var owner = _owner();
        if (owner is null) return null;

        var dialog = new SettingsDialog(current);
        return await dialog.ShowDialog<AppSettings?>(owner);
    }
}
