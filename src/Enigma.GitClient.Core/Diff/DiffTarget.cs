using System;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// What a diff compares.
/// </summary>
public enum DiffTargetKind
{
    /// <summary>A commit against one of its parents.</summary>
    Commit,

    /// <summary>Two arbitrary revisions.</summary>
    Range,

    /// <summary>The work tree against the index — what is not staged.</summary>
    WorkingTree,

    /// <summary>The index against HEAD — what is staged.</summary>
    Staged,

    /// <summary>The work tree against HEAD — everything uncommitted.</summary>
    Uncommitted,
}

/// <summary>
/// Which comparison to run.
/// </summary>
public sealed record DiffTarget
{
    private DiffTarget(DiffTargetKind kind)
        => Kind = kind;

    /// <summary>
    /// Gets what is being compared.
    /// </summary>
    public DiffTargetKind Kind { get; }

    /// <summary>
    /// Gets the commit, or the left side of a range.
    /// </summary>
    public string? From { get; private init; }

    /// <summary>
    /// Gets the right side of a range.
    /// </summary>
    public string? To { get; private init; }

    /// <summary>
    /// Gets which parent a merge is compared against, counted from zero.
    /// </summary>
    public int ParentIndex { get; private init; }

    /// <summary>
    /// Compares a commit against one of its parents.
    /// </summary>
    /// <param name="sha">The commit.</param>
    /// <param name="parentIndex">
    /// Which parent to compare against, counted from zero. The first parent is the branch the commit
    /// was made on, and is what a reviewer almost always wants to see for a merge.
    /// </param>
    /// <returns>The target.</returns>
    public static DiffTarget Commit(string sha, int parentIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        ArgumentOutOfRangeException.ThrowIfNegative(parentIndex);

        return new DiffTarget(DiffTargetKind.Commit) { From = sha, ParentIndex = parentIndex };
    }

    /// <summary>
    /// Compares two revisions.
    /// </summary>
    /// <param name="from">The older side.</param>
    /// <param name="to">The newer side.</param>
    /// <returns>The target.</returns>
    public static DiffTarget Range(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        return new DiffTarget(DiffTargetKind.Range) { From = from, To = to };
    }

    /// <summary>
    /// Compares the work tree against the index: the changes that are not staged.
    /// </summary>
    /// <returns>The target.</returns>
    public static DiffTarget WorkingTree() => new(DiffTargetKind.WorkingTree);

    /// <summary>
    /// Compares the index against HEAD: the changes that are staged.
    /// </summary>
    /// <returns>The target.</returns>
    public static DiffTarget Staged() => new(DiffTargetKind.Staged);

    /// <summary>
    /// Compares the work tree against HEAD: everything uncommitted, staged or not.
    /// </summary>
    /// <returns>The target.</returns>
    public static DiffTarget Uncommitted() => new(DiffTargetKind.Uncommitted);

    /// <inheritdoc />
    public override string ToString()
        => Kind switch
        {
            DiffTargetKind.Commit => $"{From}^{(ParentIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}..{From}",
            DiffTargetKind.Range => $"{From}..{To}",
            _ => Kind.ToString(),
        };
}
