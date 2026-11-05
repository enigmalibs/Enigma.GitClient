using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// Puts a history line's own items at the end of a badge's menu, under the badge's own.
/// </summary>
/// <remarks>
/// <para>
/// A branch or a tag badge has a menu of its own, written in XAML with live bindings, and a right-click
/// on the badge opens that menu instead of the line's. The line's items — show what it changed, create a
/// branch or a tag here, reset, revert, copy the hash — are what a reader looks for there too, so they
/// are appended to the badge's menu each time it is asked for, built from the line's entries as they
/// are at that moment (<see cref="CommitRowViewModel.BadgeMenuEntries"/>).
/// </para>
/// <para>
/// The appended items are controls, not data, so they sit in the same item list as the badge's own;
/// each carries a marker, which is how the items of an earlier opening are found and taken out first.
/// The drop menu is built the same way, with the same glyphs.
/// </para>
/// </remarks>
internal static class BadgeMenus
{
    /// <summary>
    /// What an appended item carries as its <see cref="Control.Tag"/>.
    /// </summary>
    internal static readonly object LineItem = new();

    /// <summary>
    /// Replaces what an earlier opening appended to a badge's menu with the line's entries now.
    /// </summary>
    /// <param name="menu">The badge's menu.</param>
    /// <param name="entries">The line's entries for that badge, separators included.</param>
    public static void AppendLineEntries(ContextMenu menu, IReadOnlyList<HistoryMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(entries);

        RemoveLineEntries(menu);

        foreach (HistoryMenuEntry entry in entries)
        {
            menu.Items.Add(entry.IsSeparator ? new Separator { Tag = LineItem } : ItemFor(entry));
        }
    }

    /// <summary>
    /// Takes out of a badge's menu everything an earlier opening appended.
    /// </summary>
    /// <param name="menu">The badge's menu.</param>
    public static void RemoveLineEntries(ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        for (int index = menu.Items.Count - 1; index >= 0; index--)
        {
            if (menu.Items[index] is Control { Tag: { } tag } && ReferenceEquals(tag, LineItem))
            {
                menu.Items.RemoveAt(index);
            }
        }
    }

    private static MenuItem ItemFor(HistoryMenuEntry entry)
        => new()
        {
            Header = entry.Header,
            Command = entry.Command,
            CommandParameter = entry.Parameter,
            Icon = entry.HasIcon ? HistoryPageView.MenuIcon(entry.IconKind) : null,
            Tag = LineItem,
        };
}
