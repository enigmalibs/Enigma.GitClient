using System;
using Enigma.GitClient.Core.History;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// A local branch or a remote-tracking branch, with everything the branches page and the graph
/// badge need to render it.
/// </summary>
public sealed class GitBranch : GitRef
{
    /// <summary>
    /// The ref namespace prefix of a local branch.
    /// </summary>
    public const string LocalPrefix = "refs/heads/";

    /// <summary>
    /// The ref namespace prefix of a remote-tracking branch.
    /// </summary>
    public const string RemotePrefix = "refs/remotes/";

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="fullName">The full ref name.</param>
    /// <param name="targetSha">The SHA of the branch tip.</param>
    /// <param name="isCurrent">Whether the branch is the one HEAD points at.</param>
    /// <param name="upstreamShortName">The configured upstream's short name, if any.</param>
    /// <param name="tracking">How far the branch is from its upstream.</param>
    /// <param name="tipAuthor">The author of the branch tip.</param>
    /// <param name="tipDate">The commit date of the branch tip.</param>
    /// <param name="tipSubject">The subject of the branch tip.</param>
    public GitBranch(
        string fullName,
        string targetSha,
        bool isCurrent,
        string? upstreamShortName,
        BranchTracking tracking,
        GitSignature tipAuthor,
        DateTimeOffset tipDate,
        string tipSubject)
        : base(fullName, targetSha)
    {
        ArgumentNullException.ThrowIfNull(tipAuthor);

        IsCurrent = isCurrent;
        UpstreamShortName = upstreamShortName;
        Tracking = tracking;
        TipAuthor = tipAuthor;
        TipDate = tipDate;
        TipSubject = tipSubject ?? string.Empty;
    }

    /// <summary>
    /// Gets a value indicating whether the branch lives under <c>refs/remotes</c>.
    /// </summary>
    public bool IsRemote => FullName.StartsWith(RemotePrefix, StringComparison.Ordinal);

    /// <inheritdoc />
    public override GitRefKind Kind => IsRemote ? GitRefKind.RemoteBranch : GitRefKind.LocalBranch;

    /// <inheritdoc />
    public override string ShortName
        => IsRemote
            ? FullName.Substring(RemotePrefix.Length)
            : FullName.StartsWith(LocalPrefix, StringComparison.Ordinal)
                ? FullName.Substring(LocalPrefix.Length)
                : FullName;

    /// <summary>
    /// Gets the remote a remote-tracking branch belongs to, or <see langword="null"/> for a local
    /// branch.
    /// </summary>
    public string? RemoteName
    {
        get
        {
            if (!IsRemote)
            {
                return null;
            }

            string shortName = ShortName;
            int separator = shortName.IndexOf('/');
            return separator <= 0 ? shortName : shortName.Substring(0, separator);
        }
    }

    /// <summary>
    /// Gets the branch name without its remote prefix — <c>main</c> for
    /// <c>refs/remotes/origin/main</c>.
    /// </summary>
    public string NameWithoutRemote
    {
        get
        {
            if (!IsRemote)
            {
                return ShortName;
            }

            string shortName = ShortName;
            int separator = shortName.IndexOf('/');
            return separator < 0 ? shortName : shortName.Substring(separator + 1);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the branch is the one HEAD currently points at.
    /// </summary>
    public bool IsCurrent { get; }

    /// <summary>
    /// Gets the configured upstream's short name, for example <c>origin/main</c>.
    /// </summary>
    public string? UpstreamShortName { get; }

    /// <summary>
    /// Gets how far the branch is from its upstream.
    /// </summary>
    public BranchTracking Tracking { get; }

    /// <summary>
    /// Gets the author of the branch tip.
    /// </summary>
    public GitSignature TipAuthor { get; }

    /// <summary>
    /// Gets the commit date of the branch tip.
    /// </summary>
    public DateTimeOffset TipDate { get; }

    /// <summary>
    /// Gets the subject of the branch tip.
    /// </summary>
    public string TipSubject { get; }
}

/// <summary>
/// How far a branch has diverged from its upstream.
/// </summary>
/// <param name="Ahead">Commits the branch has that its upstream does not.</param>
/// <param name="Behind">Commits the upstream has that the branch does not.</param>
/// <param name="IsUpstreamGone">
/// Whether the configured upstream no longer exists, which git reports as <c>[gone]</c>.
/// </param>
public readonly record struct BranchTracking(int Ahead, int Behind, bool IsUpstreamGone)
{
    /// <summary>
    /// A branch with no upstream, or one exactly level with it.
    /// </summary>
    public static readonly BranchTracking None = new(0, 0, false);

    /// <summary>
    /// Gets a value indicating whether the branch and its upstream have both moved on.
    /// </summary>
    public bool HasDiverged => Ahead > 0 && Behind > 0;

    /// <summary>
    /// Gets a value indicating whether there is anything to show in a tracking badge.
    /// </summary>
    public bool IsSynchronised => Ahead == 0 && Behind == 0 && !IsUpstreamGone;
}
