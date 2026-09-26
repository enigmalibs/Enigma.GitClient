using System;
using System.Globalization;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// The version of the <c>git</c> executable the client is driving.
/// </summary>
/// <param name="Major">Major version component.</param>
/// <param name="Minor">Minor version component.</param>
/// <param name="Patch">Patch version component.</param>
/// <param name="Raw">The raw version string reported by <c>git --version</c>.</param>
public readonly record struct GitVersion(int Major, int Minor, int Patch, string Raw)
    : IComparable<GitVersion>
{
    /// <summary>
    /// The oldest git release Enigma.GitClient supports. Everything the client parses —
    /// <c>status --porcelain=v2</c>, the <c>for-each-ref</c> format atoms and the
    /// <c>--pretty</c> separators — is stable from this release onwards.
    /// </summary>
    public static readonly GitVersion Minimum = new(2, 20, 0, "2.20.0");

    /// <summary>
    /// Parses the output of <c>git --version</c>.
    /// </summary>
    /// <param name="output">The raw output, for example <c>git version 2.45.1.windows.1</c>.</param>
    /// <param name="version">The parsed version when parsing succeeded.</param>
    /// <returns><see langword="true"/> when <paramref name="output"/> could be parsed.</returns>
    public static bool TryParse(string? output, out GitVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        string text = output.Trim();
        const string prefix = "git version ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(prefix.Length).Trim();
        }

        // Vendor suffixes such as "2.45.1.windows.1" or "2.39.5 (Apple Git-154)" are kept in Raw
        // but only the first three numeric components take part in comparisons.
        string raw = text;
        int space = text.IndexOf(' ');
        if (space > 0)
        {
            text = text.Substring(0, space);
        }

        string[] parts = text.Split('.');
        if (parts.Length == 0 || !TryParseComponent(parts[0], out int major))
        {
            return false;
        }

        int minor = parts.Length > 1 && TryParseComponent(parts[1], out int parsedMinor) ? parsedMinor : 0;
        int patch = parts.Length > 2 && TryParseComponent(parts[2], out int parsedPatch) ? parsedPatch : 0;

        version = new GitVersion(major, minor, patch, raw);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(GitVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    /// <summary>Compares two versions.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> precedes <paramref name="right"/>.</returns>
    public static bool operator <(GitVersion left, GitVersion right) => left.CompareTo(right) < 0;

    /// <summary>Compares two versions.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> follows <paramref name="right"/>.</returns>
    public static bool operator >(GitVersion left, GitVersion right) => left.CompareTo(right) > 0;

    /// <summary>Compares two versions.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not follow <paramref name="right"/>.</returns>
    public static bool operator <=(GitVersion left, GitVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two versions.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not precede <paramref name="right"/>.</returns>
    public static bool operator >=(GitVersion left, GitVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    private static bool TryParseComponent(string value, out int component)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component);
}
