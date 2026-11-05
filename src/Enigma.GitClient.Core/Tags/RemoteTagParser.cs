using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.Core.Tags;

/// <summary>
/// Reads what <c>git ls-remote --tags</c> says a remote has: each tag, with the commit it points at.
/// </summary>
/// <remarks>
/// <para>
/// Each line is <c>&lt;sha&gt;\t&lt;ref&gt;</c>. A lightweight tag is one line, its sha the commit's.
/// An annotated tag is two: the tag object's sha on <c>refs/tags/name</c>, then the commit it peels to
/// on <c>refs/tags/name^{}</c> — and the commit is what a tag is compared by, so the peeled line wins
/// whichever order the two come in.
/// </para>
/// <para>
/// Anything else is skipped rather than refused: a line that is not a tag, a line without a tab, a sha
/// that is not hexadecimal. What the remote says is read, not trusted.
/// </para>
/// </remarks>
public static class RemoteTagParser
{
    private const string PeeledSuffix = "^{}";

    /// <summary>
    /// Reads the output of <c>git ls-remote --tags</c>.
    /// </summary>
    /// <param name="output">The command's standard output.</param>
    /// <returns>Each tag's name, without <c>refs/tags/</c>, with the commit it points at.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Dictionary<string, string> tags = new(StringComparer.Ordinal);
        HashSet<string> peeled = new(StringComparer.Ordinal);

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int tab = line.IndexOf('\t', StringComparison.Ordinal);

            if (tab <= 0)
            {
                continue;
            }

            string sha = line[..tab];
            string reference = line[(tab + 1)..];

            if (!IsSha(sha) || !reference.StartsWith(GitTag.Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = reference[GitTag.Prefix.Length..];

            if (name.EndsWith(PeeledSuffix, StringComparison.Ordinal))
            {
                name = name[..^PeeledSuffix.Length];

                if (name.Length > 0)
                {
                    tags[name] = sha;
                    peeled.Add(name);
                }

                continue;
            }

            if (name.Length > 0 && !peeled.Contains(name))
            {
                tags[name] = sha;
            }
        }

        return tags;
    }

    private static bool IsSha(string value)
    {
        if (value.Length is not (40 or 64))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
