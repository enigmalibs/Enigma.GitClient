using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// One submodule entry of a repository's index, as <c>git ls-files --stage</c> lists it.
/// </summary>
/// <param name="Path">The submodule's folder, relative to the repository, as git printed it.</param>
/// <param name="Sha">The commit the entry records.</param>
/// <param name="Stage">
/// 0 for a resolved entry; 1 (the merge base), 2 (ours) or 3 (theirs) while a merge conflict on it is
/// open.
/// </param>
public sealed record GitlinkEntry(string Path, string Sha, int Stage);

/// <summary>
/// One submodule as <c>.gitmodules</c> declares it.
/// </summary>
/// <param name="Name">The name in the section header, <c>[submodule "&lt;name&gt;"]</c>.</param>
/// <param name="Path">The folder, relative to the repository, as written.</param>
/// <param name="Url">The address, as written; empty when none is given.</param>
public sealed record GitModulesEntry(string Name, string Path, string Url);

/// <summary>
/// Reads what git prints about submodules: <c>git config -f .gitmodules -z --get-regexp</c> and
/// <c>git ls-files --stage -z</c>.
/// </summary>
public static class SubmoduleOutputParser
{
    /// <summary>The mode git records a submodule with — a <em>gitlink</em>.</summary>
    public const string GitlinkMode = "160000";

    /// <summary>
    /// Parses <c>git ls-files --stage -z</c>: per NUL-ended record, the mode, the object, the stage, a
    /// tab and the path.
    /// </summary>
    /// <param name="output">What git printed.</param>
    /// <returns>The submodule entries (mode <c>160000</c>), in git's order; every other entry is left out.</returns>
    /// <remarks>
    /// The path follows the tab unquoted to the end of the record, so it may hold spaces, tabs and
    /// parentheses of its own.
    /// </remarks>
    public static IReadOnlyList<GitlinkEntry> ParseGitlinks(string? output)
    {
        List<GitlinkEntry> entries = [];

        if (string.IsNullOrEmpty(output))
        {
            return entries;
        }

        foreach (string record in output.Split('\0'))
        {
            int tab = record.IndexOf('\t');

            if (tab < 0 || tab + 1 >= record.Length || !record.StartsWith(GitlinkMode + " ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = record.Substring(0, tab).Split(' ');

            if (fields.Length != 3 ||
                !IsObjectName(fields[1]) ||
                fields[2] is not ("0" or "1" or "2" or "3"))
            {
                continue;
            }

            entries.Add(new GitlinkEntry(record.Substring(tab + 1), fields[1], fields[2][0] - '0'));
        }

        return entries;
    }

    /// <summary>
    /// Parses <c>git config -f .gitmodules -z --get-regexp '^submodule\.'</c>: records ended by a NUL,
    /// each a key, a newline and a value.
    /// </summary>
    /// <param name="output">What git printed.</param>
    /// <returns>
    /// The submodules that declare a path, in the order they first appear; one without a path is left
    /// out, as git ignores it.
    /// </returns>
    /// <remarks>
    /// The key is <c>submodule.&lt;name&gt;.&lt;variable&gt;</c>, and a name may hold dots and spaces, so
    /// the name runs from the first dot to the last one — which is why the NUL-separated form is read:
    /// in the plain one, a key and a value are separated by a space a name may also hold.
    /// </remarks>
    public static IReadOnlyList<GitModulesEntry> ParseGitModules(string? output)
    {
        List<string> order = [];
        Dictionary<string, (string? Path, string? Url)> entries = new(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        foreach (string record in output.Split('\0'))
        {
            int newline = record.IndexOf('\n');

            if (newline < 0)
            {
                continue;
            }

            string key = record.Substring(0, newline);
            string value = record.Substring(newline + 1);
            int lastDot = key.LastIndexOf('.');

            if (!key.StartsWith("submodule.", StringComparison.OrdinalIgnoreCase) || lastDot <= "submodule.".Length)
            {
                continue;
            }

            string name = key.Substring("submodule.".Length, lastDot - "submodule.".Length);
            string variable = key.Substring(lastDot + 1);

            if (!entries.TryGetValue(name, out (string? Path, string? Url) entry))
            {
                order.Add(name);
                entry = (null, null);
            }

            if (string.Equals(variable, "path", StringComparison.OrdinalIgnoreCase))
            {
                entry.Path = value;
            }
            else if (string.Equals(variable, "url", StringComparison.OrdinalIgnoreCase))
            {
                entry.Url = value;
            }

            entries[name] = entry;
        }

        List<GitModulesEntry> modules = new(order.Count);

        foreach (string name in order)
        {
            (string? path, string? url) = entries[name];

            if (!string.IsNullOrWhiteSpace(path))
            {
                modules.Add(new GitModulesEntry(name, NormalisePath(path), url ?? string.Empty));
            }
        }

        return modules;
    }

    /// <summary>
    /// Writes a submodule path the way git prints it: <c>/</c> separators and no trailing one.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path, normalised.</returns>
    public static string NormalisePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>
    /// Tells whether a text is a whole commit name: 40 (SHA-1) or 64 (SHA-256) hexadecimal digits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it is one.</returns>
    public static bool IsObjectName(string? text)
    {
        if (text is not { Length: 40 or 64 })
        {
            return false;
        }

        foreach (char character in text)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
