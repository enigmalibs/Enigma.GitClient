using System;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Thrown when the client refuses an operation before running git.
/// </summary>
/// <remarks>
/// This is not a git failure — git was never asked. It is the client declining to run something
/// that would lose work, or that cannot mean what the user intended: deleting the branch that is
/// checked out, deleting an unmerged branch without saying so, renaming onto a name already taken.
/// Separating it from <see cref="GitCommandException"/> is what lets the UI show a plain sentence
/// rather than git's standard error.
/// </remarks>
public sealed class GitOperationRefusedException : InvalidOperationException
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">Why the operation was refused, in words a user can act on.</param>
    public GitOperationRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">Why the operation was refused.</param>
    /// <param name="innerException">The underlying failure, when there was one.</param>
    public GitOperationRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with no message. Present for the exception-constructor pattern;
    /// prefer the overload that says why.
    /// </summary>
    public GitOperationRefusedException()
    {
    }
}
