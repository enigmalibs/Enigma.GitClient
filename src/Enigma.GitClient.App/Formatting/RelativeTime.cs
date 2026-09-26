using System;
using System.Globalization;

namespace Enigma.GitClient.App.Formatting;

/// <summary>
/// Formats a timestamp the way a history view should: relative and short, with the exact value one
/// hover away.
/// </summary>
/// <remarks>
/// A column of absolute timestamps is wide and hard to scan; what a reader actually wants from a
/// commit list is "how long ago". The absolute value still matters, so it is what the tooltip shows.
/// </remarks>
public static class RelativeTime
{
    /// <summary>
    /// Formats a timestamp relative to now.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>A short phrase such as <c>3 hours ago</c>.</returns>
    public static string Format(DateTimeOffset value) => Format(value, DateTimeOffset.Now);

    /// <summary>
    /// Formats a timestamp relative to a given moment.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    /// <param name="now">The moment to measure from.</param>
    /// <returns>A short phrase such as <c>3 hours ago</c>.</returns>
    public static string Format(DateTimeOffset value, DateTimeOffset now)
    {
        if (value == DateTimeOffset.MinValue)
        {
            return string.Empty;
        }

        TimeSpan elapsed = now - value;

        if (elapsed < TimeSpan.Zero)
        {
            // A commit dated in the future is normal after a clock change or a rewritten date; it
            // reads better as "just now" than as a negative age.
            return "just now";
        }

        if (elapsed < TimeSpan.FromSeconds(45))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromMinutes(60))
        {
            return Plural((int)Math.Round(elapsed.TotalMinutes), "minute");
        }

        if (elapsed < TimeSpan.FromHours(24))
        {
            return Plural((int)Math.Floor(elapsed.TotalHours), "hour");
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return Plural((int)Math.Floor(elapsed.TotalDays), "day");
        }

        if (elapsed < TimeSpan.FromDays(30))
        {
            return Plural((int)Math.Floor(elapsed.TotalDays / 7), "week");
        }

        if (elapsed < TimeSpan.FromDays(365))
        {
            return Plural((int)Math.Floor(elapsed.TotalDays / 30), "month");
        }

        return Plural((int)Math.Floor(elapsed.TotalDays / 365), "year");
    }

    /// <summary>
    /// Formats a timestamp in full, for the tooltip behind the relative one.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>The absolute local time, with its original offset noted.</returns>
    public static string FormatAbsolute(DateTimeOffset value)
        => value == DateTimeOffset.MinValue
            ? string.Empty
            : value.ToLocalTime().ToString("dddd d MMMM yyyy 'at' HH:mm:ss", CultureInfo.CurrentCulture);

    private static string Plural(int count, string unit)
    {
        int safe = Math.Max(1, count);

        return safe == 1
            ? $"1 {unit} ago"
            : $"{safe.ToString(CultureInfo.CurrentCulture)} {unit}s ago";
    }
}
