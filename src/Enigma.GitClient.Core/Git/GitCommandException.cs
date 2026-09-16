using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Thrown when a git invocation exits with a non-zero status.
/// </summary>
public sealed class GitCommandException : Exception
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="command">The invocation that failed.</param>
    /// <param name="exitCode">The exit code git reported.</param>
    /// <param name="standardError">Everything git wrote to standard error.</param>
    /// <param name="standardOutput">Everything git wrote to standard output.</param>
    public GitCommandException(GitCommand command, int exitCode, string standardError, string standardOutput)
        : base(BuildMessage(command, exitCode, standardError))
    {
        ArgumentNullException.ThrowIfNull(command);

        ExitCode = exitCode;
        StandardError = standardError;
        StandardOutput = standardOutput;
        Verb = command.Verb;
        RedactedArguments = ArgumentRedactor.RedactAll(command.Arguments);
        WorkingDirectory = command.WorkingDirectory;
    }

    /// <summary>
    /// Gets the exit code git reported.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets everything git wrote to standard error.
    /// </summary>
    public string StandardError { get; }

    /// <summary>
    /// Gets everything git wrote to standard output.
    /// </summary>
    public string StandardOutput { get; }

    /// <summary>
    /// Gets the sub-command that failed.
    /// </summary>
    public string Verb { get; }

    /// <summary>
    /// Gets the failing argument vector with every credential redacted.
    /// </summary>
    public IReadOnlyList<string> RedactedArguments { get; }

    /// <summary>
    /// Gets the directory the failing invocation ran in.
    /// </summary>
    public string WorkingDirectory { get; }

    private static string BuildMessage(GitCommand command, int exitCode, string standardError)
    {
        string detail = standardError.Trim();
        string commandLine = command.ToString();

        return detail.Length == 0
            ? $"'{commandLine}' exited with code {exitCode}."
            : $"'{commandLine}' exited with code {exitCode}: {detail}";
    }
}
