namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Locates the <c>git</c> executable the client drives.
/// </summary>
public interface IGitExecutable
{
    /// <summary>
    /// Gets the resolved path (or bare name) of the git executable.
    /// </summary>
    /// <exception cref="GitNotFoundException">No usable git executable could be located.</exception>
    string Path { get; }

    /// <summary>
    /// Attempts to resolve the git executable without throwing.
    /// </summary>
    /// <param name="path">The resolved path when resolution succeeded.</param>
    /// <returns><see langword="true"/> when a git executable was located.</returns>
    bool TryResolve(out string? path);
}
