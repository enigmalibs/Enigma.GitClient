using System;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// What a reference points at, which decides how the graph renders its badge.
/// </summary>
public enum GitRefKind
{
    /// <summary>A branch in <c>refs/heads</c>.</summary>
    LocalBranch,

    /// <summary>A remote-tracking branch in <c>refs/remotes</c>.</summary>
    RemoteBranch,

    /// <summary>A tag in <c>refs/tags</c>, annotated or lightweight.</summary>
    Tag,

    /// <summary>The stash, <c>refs/stash</c>.</summary>
    Stash,

    /// <summary>A note ref in <c>refs/notes</c>.</summary>
    Note,

    /// <summary>Anything else under <c>refs/</c>.</summary>
    Other,
}

/// <summary>
/// A named reference into the object database.
/// </summary>
public abstract class GitRef : IEquatable<GitRef>
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="fullName">The full ref name, for example <c>refs/heads/main</c>.</param>
    /// <param name="targetSha">The SHA of the commit the ref ultimately points at.</param>
    protected GitRef(string fullName, string targetSha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentNullException.ThrowIfNull(targetSha);

        FullName = fullName;
        TargetSha = targetSha;
    }

    /// <summary>
    /// Gets the full ref name, for example <c>refs/remotes/origin/main</c>.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Gets the SHA of the commit the ref ultimately points at. For an annotated tag this is the
    /// commit, not the tag object.
    /// </summary>
    public string TargetSha { get; }

    /// <summary>
    /// Gets the name shown to the user, with the ref namespace stripped.
    /// </summary>
    public abstract string ShortName { get; }

    /// <summary>
    /// Gets what kind of reference this is.
    /// </summary>
    public abstract GitRefKind Kind { get; }

    /// <inheritdoc />
    public bool Equals(GitRef? other)
        => other is not null &&
           other.GetType() == GetType() &&
           string.Equals(FullName, other.FullName, StringComparison.Ordinal) &&
           string.Equals(TargetSha, other.TargetSha, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GitRef);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(FullName, TargetSha);

    /// <inheritdoc />
    public override string ToString() => ShortName;
}
