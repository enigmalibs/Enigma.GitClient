using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Runs git invocations and returns their output.
/// </summary>
public interface IGitProcessRunner
{
    /// <summary>
    /// Runs a command and decodes its output as UTF-8 text.
    /// </summary>
    /// <param name="command">The invocation to run.</param>
    /// <param name="throwOnError">
    /// When <see langword="true"/> (the default), a non-zero exit code throws
    /// <see cref="GitCommandException"/>; when <see langword="false"/>, the result is returned and
    /// the caller inspects <see cref="GitResult.ExitCode"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the run and kills the child process tree.</param>
    /// <returns>The invocation's result.</returns>
    Task<GitResult> RunAsync(GitCommand command, bool throwOnError = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a command and returns its raw standard output, for output that must not pass through a
    /// text decoder.
    /// </summary>
    /// <param name="command">The invocation to run.</param>
    /// <param name="throwOnError">Whether a non-zero exit code throws.</param>
    /// <param name="cancellationToken">Cancels the run and kills the child process tree.</param>
    /// <returns>The invocation's raw result.</returns>
    Task<GitRawResult> RunRawAsync(GitCommand command, bool throwOnError = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a command and splits its standard output into records.
    /// </summary>
    /// <param name="command">The invocation to run.</param>
    /// <param name="separator">The record separator; <c>'\0'</c> for <c>-z</c> output.</param>
    /// <param name="cancellationToken">Cancels the run and kills the child process tree.</param>
    /// <returns>The records, without the trailing empty one git leaves behind.</returns>
    Task<IReadOnlyList<string>> RunLinesAsync(GitCommand command, char separator = '\n', CancellationToken cancellationToken = default);
}
