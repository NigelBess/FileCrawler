using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using FileCrawler.Views;

namespace FileCrawler.Services;

/// <summary>
/// Prompts the user to confirm a destructive action. Abstracted so view models stay view-agnostic and testable.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Shows a modal confirmation with the given title/message and a primary button labelled
    /// <paramref name="confirmText"/> (alongside a Cancel button). Returns true only if the user confirms.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Shows a modal informational message with a single acknowledge button.</summary>
    Task NotifyAsync(string title, string message, string okText = "OK");
}

/// <summary>Shows a modal <see cref="ConfirmationDialog"/> owned by a supplied window (the main window).</summary>
public sealed class DialogConfirmationService : IConfirmationService
{
    private readonly Func<Window?> _owner;

    public DialogConfirmationService(Func<Window?> owner) => _owner = owner;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var owner = _owner();
        if (owner is null) return false;

        var dialog = new ConfirmationDialog(title, message, confirmText);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task NotifyAsync(string title, string message, string okText = "OK")
    {
        var owner = _owner();
        if (owner is null) return;

        var dialog = new ConfirmationDialog(title, message, okText, showCancel: false);
        await dialog.ShowDialog<bool>(owner);
    }
}
