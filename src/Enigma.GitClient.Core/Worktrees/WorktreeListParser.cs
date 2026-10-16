using System;
using System.Collections.Generic;
using System.IO;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.Core.Worktrees;

/// <summary>
/// Reads the output of <c>git worktree list --porcelain</c>.
/// </summary>
/// <remarks>
/// <para>
/// One record per working tree, separated by blank lines, the main working tree first:
/// <c>worktree &lt;path&gt;</c>, then <c>HEAD &lt;sha&gt;</c> and <c>branch refs/heads/&lt;name&gt;</c>, or
/// <c>detached</c>, or <c>bare</c>; and, from git 2.31, <c>locked [reason]</c> and
/// <c>prunable [reason]</c>. A line this parser does not know is skipped, so a newer git's additions
/// cannot break it.
/// </para>
/// <para>
/// The parser is pure: a folder is missing only when git says it is prunable. Before git 2.31 it never
/// does, which is why <see cref="IWorktreeService.ListAsync"/> also looks at the disk. The output is
/// read without <c>-z</c> (git 2.36+), so a folder whose name holds a line break is not supported.
/// </para>
/// </remarks>
public static class WorktreeListParser
{
    /// <summary>
    /// Parses the porcelain list of a repository's working trees.
    /// </summary>
    /// <param name="porcelain">The output of <c>git worktree list --porcelain</c>, LF or CRLF.</param>
    /// <returns>The working trees in git's order, the main one first; empty for empty output.</returns>
    public static IReadOnlyList<GitWorktree> Parse(string porcelain)
    {
        ArgumentNullException.ThrowIfNull(porcelain);

        List<GitWorktree> worktrees = [];
        GitWorktree? current = null;

        foreach (string raw in porcelain.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            (string key, string value) = Split(line);

            if (key == "worktree")
            {
                Flush();
                current = new GitWorktree { Path = Normalise(value), IsMain = worktrees.Count == 0 };
                continue;
            }

            if (current is null)
            {
                continue;
            }

            current = key switch
            {
                "HEAD" => current with { HeadSha = value },
                "branch" => current with { Branch = ShortBranchName(value) },
                "detached" => current with { IsDetached = true },
                "bare" => current with { IsBare = true },
                "locked" => current with { IsLocked = true, LockReason = GitPathQuoting.Unquote(value) },
                "prunable" => current with { IsMissing = true },
                _ => current,
            };
        }

        Flush();
        return worktrees;

        void Flush()
        {
            if (current is not null)
            {
                worktrees.Add(current);
                current = null;
            }
        }
    }

    /// <summary>
    /// Turns a folder as git writes it — with <c>/</c> on Windows too — into a full path with the
    /// platform's separators and no trailing one.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>The normalised folder, or the folder as given when it cannot be read as a path.</returns>
    public static string Normalise(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static (string Key, string Value) Split(string line)
    {
        int space = line.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? (line, string.Empty) : (line.Substring(0, space), line.Substring(space + 1));
    }

    private static string ShortBranchName(string fullName)
        => fullName.StartsWith(GitBranch.LocalPrefix, StringComparison.Ordinal)
            ? fullName.Substring(GitBranch.LocalPrefix.Length)
            : fullName;
}
