using System;
using System.IO;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Finds a program in the directories of a <c>PATH</c>, the way a shell would — except that it only
/// ever answers with a rooted path, and never looks in the current directory.
/// </summary>
/// <remarks>
/// Starting a program by its bare name hands the lookup to the operating system, and both
/// <c>CreateProcess</c> on Windows and .NET's own resolution on Unix look in the current directory
/// before the <c>PATH</c>. The current directory is often a repository, and a repository is content
/// somebody else wrote: a <c>git.exe</c> or a <c>cmd.exe</c> committed at its root would run in place
/// of the real one. Searching here instead, and starting what is found by its full path, takes the
/// current directory out of the lookup — and with it a <c>PATH</c> entry that is not fully qualified,
/// such as <c>.</c>, which is the current directory under another name.
/// </remarks>
public static class ExecutableSearch
{
    /// <summary>
    /// Finds a program in the fully qualified directories of a <c>PATH</c>.
    /// </summary>
    /// <param name="fileName">The program's file name, with its extension where the platform needs one (<c>git.exe</c>).</param>
    /// <param name="pathVariable">The <c>PATH</c> to search, or <see langword="null"/> for none.</param>
    /// <param name="fileExists">Says whether a file exists; <see cref="File.Exists(string)"/> when omitted.</param>
    /// <returns>The first rooted path that exists, or <see langword="null"/> when no directory has it.</returns>
    public static string? FindOnPath(string fileName, string? pathVariable, Func<string, bool>? fileExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        Func<string, bool> exists = fileExists ?? File.Exists;

        foreach (string entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = entry.Trim().Trim('"');

            // "." or "bin" would be resolved against the current directory: the very lookup this
            // search exists to avoid.
            if (directory.Length == 0 || !Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            string candidate;

            try
            {
                candidate = Path.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                // A malformed entry is skipped rather than failing the whole search.
                continue;
            }

            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
