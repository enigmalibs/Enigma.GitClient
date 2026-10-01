using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Configuration;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// Orders the lines of a reference list — branches, tags — the way the reader chose.
/// </summary>
public static class RefSort
{
    /// <summary>
    /// Orders items by name or by date, either way.
    /// </summary>
    /// <typeparam name="T">The kind of line.</typeparam>
    /// <param name="items">The lines.</param>
    /// <param name="name">A line's name.</param>
    /// <param name="date">The date of the commit a line points at.</param>
    /// <param name="key">What to order by.</param>
    /// <param name="direction">Which way.</param>
    /// <returns>The lines, in order.</returns>
    /// <remarks>
    /// Names compare without regard to case, as the lists always did, and the name breaks a tie between
    /// two equal dates — always A to Z, so lines that share a commit keep one order whichever way the
    /// dates run.
    /// </remarks>
    public static List<T> Order<T>(
        IEnumerable<T> items,
        Func<T, string> name,
        Func<T, DateTimeOffset> date,
        RefSortKey key,
        SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(date);

        bool descending = direction == SortDirection.Descending;

        IOrderedEnumerable<T> ordered = key switch
        {
            RefSortKey.Date when descending => items.OrderByDescending(date).ThenBy(name, StringComparer.OrdinalIgnoreCase),
            RefSortKey.Date => items.OrderBy(date).ThenBy(name, StringComparer.OrdinalIgnoreCase),
            _ when descending => items.OrderByDescending(name, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderBy(name, StringComparer.OrdinalIgnoreCase),
        };

        return [.. ordered];
    }

    /// <summary>
    /// What the direction button says it does, for the key in use.
    /// </summary>
    /// <param name="key">What the list is ordered by.</param>
    /// <param name="direction">Which way.</param>
    /// <returns>The order in words.</returns>
    public static string Describe(RefSortKey key, SortDirection direction)
        => (key, direction) switch
        {
            (RefSortKey.Date, SortDirection.Descending) => "Newest first",
            (RefSortKey.Date, _) => "Oldest first",
            (_, SortDirection.Descending) => "Z to A",
            _ => "A to Z",
        };
}
