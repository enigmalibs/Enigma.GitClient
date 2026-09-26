using System;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// The outcome of a git invocation whose output is binary — a blob, a patch of unknown encoding, or
/// anything else that must not pass through a text decoder.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The raw bytes the process wrote to standard output.</param>
/// <param name="StandardError">Everything the process wrote to standard error.</param>
/// <param name="Duration">How long the process ran.</param>
public sealed record GitRawResult(int ExitCode, byte[] StandardOutput, string StandardError, TimeSpan Duration)
{
    /// <summary>
    /// Gets a value indicating whether git reported success.
    /// </summary>
    public bool IsSuccess => ExitCode == 0;
}
