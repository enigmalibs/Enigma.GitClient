using System;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Thrown when no usable <c>git</c> executable can be found. Enigma.GitClient drives the real git
/// binary, so this is a fatal, user-actionable condition rather than a recoverable error.
/// </summary>
public sealed class GitNotFoundException : Exception
{
    /// <summary>
    /// Initialises a new instance with a generic message.
    /// </summary>
    public GitNotFoundException()
        : base("No usable 'git' executable could be found.")
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">The reason git could not be located.</param>
    public GitNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the failure that caused it — the operating system's refusal to
    /// start the executable, whose error code says whether it was missing, denied or not a program.
    /// </summary>
    /// <param name="message">The reason git could not be located or started.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public GitNotFoundException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
