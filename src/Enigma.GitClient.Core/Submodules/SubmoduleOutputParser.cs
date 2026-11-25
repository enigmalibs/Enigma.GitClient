using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Git;

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

    /// <summary>
    /// Reads the gitlinks <c>git diff --raw -z</c> reports as changed, with both sides.
    /// </summary>
    /// <param name="output">What git printed.</param>
    /// <returns>The path, the old commit and the new one — empty for a side without the submodule.</returns>
    public static IReadOnlyList<(string Path, string OldSha, string NewSha)> ParseChangedGitlinkEntries(string? output)
    {
        List<(string, string, string)> entries = [];

        if (string.IsNullOrEmpty(output))
        {
            return entries;
        }

        string[] fields = output.Split('\0');

        for (int index = 0; index + 1 < fields.Length; index++)
        {
            string header = fields[index];

            if (!header.StartsWith(':'))
            {
                continue;
            }

            string[] parts = header[1..].Split(' ');
            string path = fields[++index];

            if (parts.Length < 4 || (parts[0] != SubmoduleOutputParser.GitlinkMode && parts[1] != SubmoduleOutputParser.GitlinkMode))
            {
                continue;
            }

            entries.Add((path, OrEmpty(parts[0], parts[2]), OrEmpty(parts[1], parts[3])));
        }

        return entries;

        static string OrEmpty(string mode, string sha)
            => mode == SubmoduleOutputParser.GitlinkMode && sha.Trim('0').Length > 0 ? sha : string.Empty;
    }

    /// <summary>
    /// Reads the gitlinks of <c>git ls-tree -z</c>'s output, with the commit each records.
    /// </summary>
    /// <param name="output">What git printed.</param>
    /// <returns>The path and the commit of each submodule, in git's order.</returns>
    public static IReadOnlyList<(string Path, string Sha)> ParseTreeGitlinkEntries(string? output)
    {
        List<(string, string)> entries = [];

        if (string.IsNullOrEmpty(output))
        {
            return entries;
        }

        foreach (string record in output.Split('\0'))
        {
            int tab = record.IndexOf('\t');

            if (tab < 0 || !record.StartsWith(SubmoduleOutputParser.GitlinkMode + " ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = record[..tab].Split(' ');

            if (fields.Length == 3 && SubmoduleOutputParser.IsObjectName(fields[2]))
            {
                entries.Add((record[(tab + 1)..], fields[2]));
            }
        }

        return entries;
    }

    /// <summary>
    /// Reads <c>git log --format=%h%x1f%s%x1f%an%x1f%aI</c>'s records.
    /// </summary>
    /// <param name="output">What git printed, one commit per line.</param>
    /// <returns>The commits.</returns>
    public static IReadOnlyList<PinCommit> ParsePinCommits(string? output)
    {
        List<PinCommit> commits = [];

        if (string.IsNullOrEmpty(output))
        {
            return commits;
        }

        foreach (string line in output.Split('\n'))
        {
            string[] fields = line.TrimEnd('\r').Split('\u001f');

            if (fields.Length == 4 && DateTimeOffset.TryParse(fields[3], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset date))
            {
                commits.Add(new PinCommit(fields[0], fields[1], fields[2], date));
            }
        }

        return commits;
    }

    /// <summary>
    /// Reads the paths of the gitlinks <c>git diff-tree -r --no-renames -z</c> reports as changed.
    /// </summary>
    /// <param name="output">
    /// What git printed: for each change, <c>:&lt;old mode&gt; &lt;new mode&gt; &lt;old&gt; &lt;new&gt;
    /// &lt;status&gt;</c>, a NUL, the path and a NUL.
    /// </param>
    /// <returns>The paths where either side is a submodule (mode <c>160000</c>), in git's order.</returns>
    public static IReadOnlyList<string> ParseChangedGitlinks(string? output)
    {
        List<string> paths = [];

        if (string.IsNullOrEmpty(output))
        {
            return paths;
        }

        string[] fields = output.Split('\0');

        for (int index = 0; index + 1 < fields.Length; index++)
        {
            string header = fields[index];

            if (!header.StartsWith(':'))
            {
                continue;
            }

            string[] parts = header.Substring(1).Split(' ');
            string path = fields[++index];

            if (parts.Length >= 2 && (parts[0] == SubmoduleOutputParser.GitlinkMode || parts[1] == SubmoduleOutputParser.GitlinkMode))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Reads the paths of the gitlinks in <c>git ls-tree -r -z</c>'s output.
    /// </summary>
    /// <param name="output">What git printed: <c>&lt;mode&gt; &lt;type&gt; &lt;object&gt;</c>, a tab and the path, per NUL-ended record.</param>
    /// <returns>The submodules' paths, in git's order.</returns>
    public static IReadOnlyList<string> ParseGitlinksInTree(string? output)
    {
        List<string> paths = [];

        if (string.IsNullOrEmpty(output))
        {
            return paths;
        }

        foreach (string record in output.Split('\0'))
        {
            int tab = record.IndexOf('\t');

            if (tab > 0 && record.StartsWith(SubmoduleOutputParser.GitlinkMode + " ", StringComparison.Ordinal))
            {
                paths.Add(record.Substring(tab + 1));
            }
        }

        return paths;
    }

    /// <summary>
    /// Summarises what git wrote while failing to update a submodule: its errors, or else its last line
    /// — never the progress chatter before them.
    /// </summary>
    /// <param name="standardError">Everything git wrote to standard error.</param>
    /// <returns>The message, with any credential redacted.</returns>
    public static string SummariseFailure(string? standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return "git could not update it.";
        }

        List<string> errors = [];
        string last = string.Empty;

        foreach (string raw in standardError.Split('\n', '\r'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            last = line;

            if (line.StartsWith("fatal:", StringComparison.Ordinal) || line.StartsWith("error:", StringComparison.Ordinal))
            {
                errors.Add(line);
            }
        }

        return ArgumentRedactor.Redact(errors.Count > 0 ? string.Join(Environment.NewLine, errors) : last);
    }
}
