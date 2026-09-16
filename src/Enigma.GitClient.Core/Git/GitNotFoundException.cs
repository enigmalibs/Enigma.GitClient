using System;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Thrown when no usable <c>git</c> executable can be found. Enigma.GitClient drives the real git
/// binary, so this is a fatal, user-actionable condition rather than a recoverable error.
/// </summary>
public sealed class GitNotFoundException : Exception
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">The reason git could not be located.</param>
    public GitNotFoundException(string message)
        : base(message)
    {
    }
}
