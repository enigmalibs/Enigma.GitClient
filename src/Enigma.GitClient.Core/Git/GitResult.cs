using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// The outcome of a git invocation whose output is text.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything the process wrote to standard output.</param>
/// <param name="StandardError">Everything the process wrote to standard error.</param>
/// <param name="Duration">How long the process ran.</param>
public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)
{
    /// <summary>
    /// Gets a value indicating whether git reported success.
    /// </summary>
    public bool IsSuccess => ExitCode == 0;

    /// <summary>
    /// Gets standard output with a single trailing newline removed, which is what almost every
    /// caller wants from a git command.
    /// </summary>
    public string TrimmedOutput => StandardOutput.TrimEnd('\n', '\r');

    /// <summary>
    /// Splits standard output into records on a separator, dropping the trailing empty record git
    /// leaves behind.
    /// </summary>
    /// <param name="separator">The record separator.</param>
    /// <returns>The non-terminal records.</returns>
    public IReadOnlyList<string> SplitOutput(char separator = '\n')
    {
        if (StandardOutput.Length == 0)
        {
            return [];
        }

        string[] parts = StandardOutput.Split(separator);
        List<string> records = new(parts.Length);

        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];

            // git terminates its last record with the separator, which yields a trailing empty
            // entry; genuinely empty interior records are preserved.
            if (index == parts.Length - 1 && part.Length == 0)
            {
                continue;
            }

            records.Add(separator == '\n' ? part.TrimEnd('\r') : part);
        }

        return records;
    }
}
