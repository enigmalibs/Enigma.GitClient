using System;
using System.Collections.Generic;
using System.Text;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// Reverses the C-style quoting git applies to a path it cannot write literally.
/// </summary>
/// <remarks>
/// The client runs every command with <c>core.quotePath=false</c>, so non-ASCII paths arrive as raw
/// UTF-8. git still quotes a path containing a double quote, a backslash or a control character,
/// and it always quotes in the <c>---</c>/<c>+++</c> and <c>rename from/to</c> lines when it has to,
/// so a parser that ignores quoting corrupts exactly the paths that are hardest to get right by hand.
/// </remarks>
public static class GitPathQuoting
{
    /// <summary>
    /// Removes git's quoting from a path, if it is quoted.
    /// </summary>
    /// <param name="value">The raw path as it appeared in the patch.</param>
    /// <returns>The literal path.</returns>
    public static string Unquote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return value;
        }

        string inner = value.Substring(1, value.Length - 2);

        // Octal escapes encode individual UTF-8 bytes, so they are decoded into a byte buffer and
        // the whole result is interpreted as UTF-8 at the end — decoding each escape separately
        // would mangle every multi-byte character.
        List<byte> bytes = new(inner.Length);
        int index = 0;

        while (index < inner.Length)
        {
            char current = inner[index];

            if (current != '\\')
            {
                AppendUtf8(bytes, current);
                index++;
                continue;
            }

            index++;
            if (index >= inner.Length)
            {
                AppendUtf8(bytes, '\\');
                break;
            }

            char escape = inner[index];
            index++;

            switch (escape)
            {
                case 'a':
                    bytes.Add(0x07);
                    break;
                case 'b':
                    bytes.Add(0x08);
                    break;
                case 'f':
                    bytes.Add(0x0C);
                    break;
                case 'n':
                    bytes.Add(0x0A);
                    break;
                case 'r':
                    bytes.Add(0x0D);
                    break;
                case 't':
                    bytes.Add(0x09);
                    break;
                case 'v':
                    bytes.Add(0x0B);
                    break;
                case '"':
                case '\\':
                    bytes.Add((byte)escape);
                    break;
                default:
                    if (escape is >= '0' and <= '7')
                    {
                        int octal = escape - '0';
                        int digits = 1;

                        while (digits < 3 && index < inner.Length && inner[index] is >= '0' and <= '7')
                        {
                            octal = (octal * 8) + (inner[index] - '0');
                            index++;
                            digits++;
                        }

                        bytes.Add((byte)(octal & 0xFF));
                    }
                    else
                    {
                        // Not an escape git produces; keep both characters rather than losing data.
                        AppendUtf8(bytes, '\\');
                        AppendUtf8(bytes, escape);
                    }

                    break;
            }
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>
    /// Removes a <c>a/</c> or <c>b/</c> style prefix from a patch path, and unquotes it.
    /// </summary>
    /// <param name="value">The raw path as it appeared in the patch.</param>
    /// <returns>
    /// The repository-relative path, or <see langword="null"/> when the path is
    /// <c>/dev/null</c> — which is how git says "this side does not exist".
    /// </returns>
    public static string? UnquoteAndStripPrefix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string unquoted = Unquote(value.Trim());

        if (string.Equals(unquoted, "/dev/null", StringComparison.Ordinal))
        {
            return null;
        }

        // git uses a/ and b/ by default, and honours diff.noprefix and custom prefixes; anything
        // that is not one of the two known prefixes is left alone rather than silently truncated.
        if (unquoted.StartsWith("a/", StringComparison.Ordinal) ||
            unquoted.StartsWith("b/", StringComparison.Ordinal) ||
            unquoted.StartsWith("c/", StringComparison.Ordinal) ||
            unquoted.StartsWith("i/", StringComparison.Ordinal) ||
            unquoted.StartsWith("w/", StringComparison.Ordinal) ||
            unquoted.StartsWith("o/", StringComparison.Ordinal))
        {
            return unquoted.Substring(2);
        }

        return unquoted;
    }

    private static void AppendUtf8(List<byte> bytes, char value)
    {
        if (value < 0x80)
        {
            bytes.Add((byte)value);
            return;
        }

        bytes.AddRange(Encoding.UTF8.GetBytes(value.ToString()));
    }
}
