using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// An immutable git invocation: a working directory, the full argument vector and any environment
/// overrides. Arguments are always handed to the process as a vector, never as a command line, so
/// no value a user types can be interpreted as a shell construct.
/// </summary>
public sealed class GitCommand
{
    private readonly ReadOnlyCollection<string> _arguments;

    /// <summary>
    /// Initialises a new invocation.
    /// </summary>
    /// <param name="workingDirectory">The directory the process runs in.</param>
    /// <param name="arguments">The complete argument vector, excluding the executable itself.</param>
    /// <param name="environment">Extra environment variables to set on the child process.</param>
    /// <param name="standardInput">Text written to the child process' standard input, if any.</param>
    public GitCommand(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        string? standardInput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            throw new ArgumentException("A git command needs at least one argument.", nameof(arguments));
        }

        WorkingDirectory = workingDirectory;
        _arguments = new ReadOnlyCollection<string>([.. arguments]);
        Environment = environment ?? new Dictionary<string, string>(0);
        StandardInput = standardInput;
        Verb = GitArgumentVector.FindVerb(_arguments) ?? string.Empty;
    }

    /// <summary>
    /// Gets the directory the git process runs in.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the complete argument vector passed to the executable.
    /// </summary>
    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>
    /// Gets the git sub-command this invocation runs, for example <c>log</c> or <c>status</c>.
    /// </summary>
    public string Verb { get; }

    /// <summary>
    /// Gets the environment variables set on the child process in addition to the defaults.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Gets the text written to the child process' standard input, or <see langword="null"/> when
    /// standard input is not used.
    /// </summary>
    public string? StandardInput { get; }

    /// <summary>
    /// Renders the invocation with every credential redacted, for logs and error messages.
    /// </summary>
    /// <returns>A redacted, human-readable command line.</returns>
    public override string ToString() => $"git {ArgumentRedactor.RedactCommandLine(_arguments)}";
}
