namespace Enigma.GitClient.Core.Git;

/// <summary>
/// The result of probing the host for a usable git installation.
/// </summary>
/// <param name="Status">What the probe found.</param>
/// <param name="Version">The installed version, when one could be read.</param>
/// <param name="ExecutablePath">The executable that was probed, when one was resolved.</param>
/// <param name="Message">A user-facing explanation, empty when git is usable.</param>
public sealed record GitAvailability(
    GitAvailabilityStatus Status,
    GitVersion? Version,
    string? ExecutablePath,
    string Message)
{
    /// <summary>
    /// Gets a value indicating whether the installed git can drive the client.
    /// </summary>
    public bool IsUsable => Status == GitAvailabilityStatus.Available;
}

/// <summary>
/// What a git availability probe found.
/// </summary>
public enum GitAvailabilityStatus
{
    /// <summary>A git executable of a supported version is available.</summary>
    Available,

    /// <summary>No git executable could be found or started.</summary>
    NotFound,

    /// <summary>A git executable was found, but it is older than the supported minimum.</summary>
    TooOld,
}
