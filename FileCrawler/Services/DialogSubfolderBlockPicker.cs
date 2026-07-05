using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using FileCrawler.Views;

namespace FileCrawler.Services;

/// <summary>
/// Shows the block-level choices in a modal dialog owned by a supplied window (the main window).
/// </summary>
public sealed class DialogSubfolderBlockPicker : ISubfolderBlockPicker
{
    private readonly Func<Window?> _owner;

    public DialogSubfolderBlockPicker(Func<Window?> owner) => _owner = owner;

    public async Task<string?> PickAsync(IReadOnlyList<string> candidatePaths)
    {
        var owner = _owner();
        if (owner is null || candidatePaths.Count == 0) return null;

        var dialog = new BlockSubfolderDialog(candidatePaths);
        return await dialog.ShowDialog<string?>(owner);
    }
}
