using System;
using System.Diagnostics;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// Reads a real repository back with git itself.
/// </summary>
/// <remarks>
/// What a merge did is a fact about the commit graph, not about what the client reported: a test that
/// tells a merge from a fast-forward asks git how many parents the commit has.
/// </remarks>
public static class GitProbe
{
    /// <summary>
    /// Counts the parents of a commit — one for an ordinary commit, which is also where a
    /// fast-forward leaves the branch, and two for a merge commit.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="revision">The commit to look at.</param>
    /// <returns>How many parents the commit has.</returns>
    public static int ParentCount(RepositoryHandle repository, string revision = "HEAD")
    {
        ArgumentNullException.ThrowIfNull(repository);

        // "<sha> <parent> <parent>…": everything after the commit's own hash is a parent.
        return Read(repository, "rev-list", "--parents", "-1", revision)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Length - 1;
    }

    /// <summary>
    /// Resolves a revision to its full hash.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="revision">The revision to resolve.</param>
    /// <returns>The full hash.</returns>
    public static string Sha(RepositoryHandle repository, string revision)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return Read(repository, "rev-parse", revision);
    }

    private static string Read(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}
