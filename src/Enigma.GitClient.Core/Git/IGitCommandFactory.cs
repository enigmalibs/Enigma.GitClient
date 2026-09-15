using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Builds git invocations with the client's standard global options applied and the forbidden
/// operations rejected.
/// </summary>
public interface IGitCommandFactory
{
    /// <summary>
    /// Builds an invocation for a repository.
    /// </summary>
    /// <param name="workingDirectory">The directory the process runs in.</param>
    /// <param name="arguments">The sub-command and its arguments, without the global options.</param>
    /// <returns>The invocation, with the standard global options prepended.</returns>
    GitCommand Create(string workingDirectory, params string[] arguments);

    /// <summary>
    /// Builds an invocation for a repository.
    /// </summary>
    /// <param name="workingDirectory">The directory the process runs in.</param>
    /// <param name="arguments">The sub-command and its arguments, without the global options.</param>
    /// <returns>The invocation, with the standard global options prepended.</returns>
    GitCommand Create(string workingDirectory, IEnumerable<string> arguments);

    /// <summary>
    /// Builds an invocation that writes text to git's standard input.
    /// </summary>
    /// <param name="workingDirectory">The directory the process runs in.</param>
    /// <param name="standardInput">The text written to standard input.</param>
    /// <param name="arguments">The sub-command and its arguments.</param>
    /// <returns>The invocation.</returns>
    GitCommand CreateWithInput(string workingDirectory, string standardInput, IEnumerable<string> arguments);
}
