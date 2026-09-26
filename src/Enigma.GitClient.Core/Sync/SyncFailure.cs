using System;

namespace Enigma.GitClient.Core.Sync;

/// <summary>
/// Why a network operation failed, in terms a user can act on.
/// </summary>
public enum SyncFailureKind
{
    /// <summary>git failed for a reason this version does not model.</summary>
    Unknown,

    /// <summary>The remote refused the credentials, or there were none to offer.</summary>
    Authentication,

    /// <summary>The host's key is not the one that is known, or is not known at all.</summary>
    HostKey,

    /// <summary>The remote already has commits this push would drop.</summary>
    NonFastForward,

    /// <summary>The lease no longer holds: the remote moved since it was last fetched.</summary>
    StaleLease,

    /// <summary>The branch has no upstream to pull from or push to.</summary>
    NoUpstream,

    /// <summary>The host could not be reached at all.</summary>
    Network,

    /// <summary>The remote name does not exist here.</summary>
    UnknownRemote,

    /// <summary>The merge a pull started stopped on conflicts.</summary>
    MergeConflict,

    /// <summary>The operation would overwrite uncommitted work.</summary>
    LocalChanges,
}

/// <summary>
/// A failed network operation, classified.
/// </summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Message">What to tell the user, and what they can do about it.</param>
/// <param name="Detail">Everything git wrote, kept for the log and for the unmodelled cases.</param>
public sealed record SyncFailure(SyncFailureKind Kind, string Message, string Detail)
{
    /// <summary>
    /// Gets a value indicating whether the failure is one the user can fix by retrying after doing
    /// something, rather than one that needs their credentials or their host configuration.
    /// </summary>
    public bool IsRecoverableLocally
        => Kind is SyncFailureKind.NonFastForward
            or SyncFailureKind.StaleLease
            or SyncFailureKind.MergeConflict
            or SyncFailureKind.LocalChanges
            or SyncFailureKind.NoUpstream;
}

/// <summary>
/// Thrown when a network operation fails in a way worth naming.
/// </summary>
public sealed class SyncException : Exception
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="failure">What went wrong.</param>
    /// <param name="innerException">The underlying git failure, when there was one.</param>
    public SyncException(SyncFailure failure, Exception? innerException = null)
        : base(failure?.Message ?? "The operation failed.", innerException)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    /// <summary>
    /// Initialises a new instance with a message and no classification.
    /// </summary>
    /// <param name="message">The message.</param>
    public SyncException(string message)
        : base(message)
        => Failure = new SyncFailure(SyncFailureKind.Unknown, message, message);

    /// <summary>
    /// Initialises a new instance with a message and an inner exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public SyncException(string message, Exception innerException)
        : base(message, innerException)
        => Failure = new SyncFailure(SyncFailureKind.Unknown, message, message);

    /// <summary>
    /// Initialises a new instance with nothing said. Present for the exception-constructor pattern.
    /// </summary>
    public SyncException()
        => Failure = new SyncFailure(SyncFailureKind.Unknown, "The operation failed.", string.Empty);

    /// <summary>
    /// Gets what went wrong.
    /// </summary>
    public SyncFailure Failure { get; }
}

/// <summary>
/// Turns git's standard error into a classified failure.
/// </summary>
/// <remarks>
/// git's diagnostics are written for a terminal and for someone who already knows git. The five
/// failures that actually happen — no credentials, an unknown host key, a rejected push, no
/// upstream, no network — deserve a sentence that says what to do next. Everything else is passed
/// through verbatim rather than guessed at, because a wrong explanation is worse than git's own.
/// </remarks>
public static class SyncErrorMapper
{
    /// <summary>
    /// Classifies a failure.
    /// </summary>
    /// <param name="standardError">Everything git wrote to standard error.</param>
    /// <param name="standardOutput">Everything git wrote to standard output.</param>
    /// <returns>The classified failure.</returns>
    public static SyncFailure Map(string standardError, string standardOutput = "")
    {
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(standardOutput);

        string text = $"{standardError}\n{standardOutput}";

        if (Contains(text, "could not read Username", "Authentication failed", "Permission denied (publickey",
                "terminal prompts disabled", "invalid username or password", "Support for password authentication was removed"))
        {
            return new SyncFailure(
                SyncFailureKind.Authentication,
                "The remote refused your credentials. Check the credential helper or the SSH key this remote uses, "
                + "then try again.",
                standardError);
        }

        if (Contains(text, "Host key verification failed", "REMOTE HOST IDENTIFICATION HAS CHANGED",
                "no matching host key type"))
        {
            return new SyncFailure(
                SyncFailureKind.HostKey,
                "The host's key is not the one this machine knows. Verify the host and add its key to your "
                + "known_hosts before trying again.",
                standardError);
        }

        if (Contains(text, "stale info", "does not match any"))
        {
            return new SyncFailure(
                SyncFailureKind.StaleLease,
                "The remote has moved since you last fetched, so the push was refused to avoid overwriting work "
                + "you have not seen. Fetch, look at what arrived, then push again.",
                standardError);
        }

        if (Contains(text, "non-fast-forward", "Updates were rejected", "fetch first", "behind its remote counterpart"))
        {
            return new SyncFailure(
                SyncFailureKind.NonFastForward,
                "The remote has commits this push would drop. Pull first, then push.",
                standardError);
        }

        if (Contains(text, "no upstream", "There is no tracking information", "no tracking information for the current branch"))
        {
            return new SyncFailure(
                SyncFailureKind.NoUpstream,
                "This branch has no upstream. Push it once with tracking, or set its upstream, and the rest will "
                + "follow.",
                standardError);
        }

        if (Contains(text, "Could not resolve host", "Connection refused", "Connection timed out",
                "Network is unreachable", "unable to access", "Failed to connect"))
        {
            return new SyncFailure(
                SyncFailureKind.Network,
                "The remote could not be reached. Check the connection and the remote's URL.",
                standardError);
        }

        if (Contains(text, "does not appear to be a git repository", "Could not read from remote repository",
                "No such remote"))
        {
            return new SyncFailure(
                SyncFailureKind.UnknownRemote,
                "That remote could not be read. Check its URL, and that you have access to it.",
                standardError);
        }

        if (Contains(text, "Automatic merge failed", "CONFLICT (", "fix conflicts and then commit"))
        {
            return new SyncFailure(
                SyncFailureKind.MergeConflict,
                "The merge stopped on conflicts. Resolve them, then commit the merge.",
                standardError);
        }

        if (Contains(text, "Your local changes to the following files would be overwritten",
                "commit your changes or stash them", "cannot pull with rebase", "You have unstaged changes"))
        {
            return new SyncFailure(
                SyncFailureKind.LocalChanges,
                "Uncommitted work is in the way. Commit it or stash it, then try again.",
                standardError);
        }

        return new SyncFailure(SyncFailureKind.Unknown, FirstMeaningfulLine(standardError), standardError);
    }

    private static bool Contains(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Picks the line worth showing out of git's standard error.
    /// </summary>
    /// <param name="standardError">Everything git wrote.</param>
    /// <returns>The line, or a fallback when git said nothing.</returns>
    /// <remarks>
    /// The first line is often "fatal:" with the reason after it, but it is just as often a progress
    /// remnant or a hint. The first line that starts with a diagnostic prefix wins; otherwise the
    /// first non-empty line does.
    /// </remarks>
    public static string FirstMeaningfulLine(string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);

        string[] lines = standardError.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return "git reported no reason.";
    }
}
