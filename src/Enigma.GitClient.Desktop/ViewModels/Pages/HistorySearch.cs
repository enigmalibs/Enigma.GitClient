using System;
using System.Collections.Generic;
using System.Globalization;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// The history's search over the loaded lines: which lines match, where the selected one stands among
/// them, the next one up or down, and what the search bar says.
/// </summary>
/// <remarks>
/// The logic only: the search text, the match count and the selection are the page's, which the
/// view binds. What a line matches on — the message, the start of the SHA, the author — is the line's
/// own <see cref="CommitRowViewModel.Matches"/>.
/// </remarks>
internal static class HistorySearch
{
    /// <summary>
    /// Marks the lines the search finds, and counts them.
    /// </summary>
    /// <param name="rows">The loaded lines.</param>
    /// <param name="searchText">What is searched for, as typed.</param>
    /// <returns>How many lines match; none for an empty search.</returns>
    public static int Mark(IEnumerable<CommitRowViewModel> rows, string searchText)
    {
        ArgumentNullException.ThrowIfNull(rows);

        string search = (searchText ?? string.Empty).Trim();
        int found = 0;

        foreach (CommitRowViewModel row in rows)
        {
            bool matches = search.Length > 0 && row.Matches(search);
            row.IsSearchMatch = matches;

            if (matches)
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>
    /// Gets the selected line's place among the lines the search found, counted from 1 down the list.
    /// </summary>
    /// <param name="rows">The loaded lines.</param>
    /// <param name="selected">The selected line, if any.</param>
    /// <returns>Its place; 0 when it is not one of them.</returns>
    public static int PositionOf(IEnumerable<CommitRowViewModel> rows, CommitRowViewModel? selected)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (selected is not { IsSearchMatch: true })
        {
            return 0;
        }

        int position = 0;

        foreach (CommitRowViewModel row in rows)
        {
            if (row.IsSearchMatch)
            {
                position++;

                if (ReferenceEquals(row, selected))
                {
                    return position;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Finds the next or the previous line the search found, wrapping around the loaded lines.
    /// </summary>
    /// <param name="rows">The loaded lines.</param>
    /// <param name="selected">The selected line, if any: where the step starts from.</param>
    /// <param name="forward">Down the list when <see langword="true"/>, up it otherwise.</param>
    /// <returns>The line, or <see langword="null"/> when none matches.</returns>
    public static CommitRowViewModel? Next(IReadOnlyList<CommitRowViewModel> rows, CommitRowViewModel? selected, bool forward)
    {
        ArgumentNullException.ThrowIfNull(rows);

        int count = rows.Count;

        if (count == 0)
        {
            return null;
        }

        // From the selected line; with none, from just above the first line going down, or just below
        // the last going up, so the first step lands on the first or the last line.
        int current = -1;

        if (selected is not null)
        {
            for (int index = 0; index < count; index++)
            {
                if (ReferenceEquals(rows[index], selected))
                {
                    current = index;
                    break;
                }
            }
        }

        int origin = current >= 0 ? current : forward ? -1 : count;

        for (int step = 1; step <= count; step++)
        {
            int index = (((forward ? origin + step : origin - step) % count) + count) % count;

            if (rows[index].IsSearchMatch)
            {
                return rows[index];
            }
        }

        return null;
    }

    /// <summary>
    /// What the search bar says beside its box: <c>match 3/12</c>, <c>match –/12</c> when the selected
    /// line is not one of them, <c>no match</c>, or nothing while nothing is searched for.
    /// </summary>
    /// <param name="hasSearch">Whether anything is searched for.</param>
    /// <param name="matchCount">How many lines match.</param>
    /// <param name="position">The selected line's place among them; 0 for none.</param>
    /// <returns>The words.</returns>
    public static string Summarise(bool hasSearch, int matchCount, int position)
    {
        if (!hasSearch)
        {
            return string.Empty;
        }

        if (matchCount == 0)
        {
            return "no match";
        }

        string current = position > 0 ? position.ToString(CultureInfo.CurrentCulture) : "–";

        return $"match {current}/{matchCount.ToString(CultureInfo.CurrentCulture)}";
    }
}
