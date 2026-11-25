using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Stashes;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// How the history draws the stash: each entry as one line off the commit it was made on, and the
/// other commits git records an entry with — its index, its untracked files — as no line at all.
/// </summary>
/// <remarks>
/// <para>
/// It holds the stash as the last reload read it, by the commit each entry is recorded as, and the
/// helper commits found as the pages arrive. A stash is drawn as one line, as GitKraken draws it: those
/// commits are git's bookkeeping, not history anyone made.
/// </para>
/// <para>
/// Owned by the history page, and used on its thread only.
/// </para>
/// </remarks>
internal sealed class StashFolding
{
    private readonly HashSet<string> _helpers = new(StringComparer.Ordinal);
    private Dictionary<string, StashEntry> _bySha = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the stash as the last reload read it, newest first.
    /// </summary>
    public IReadOnlyList<StashEntry> Entries { get; private set; } = [];

    /// <summary>
    /// Takes the stash a reload read.
    /// </summary>
    /// <param name="stashes">The entries, newest first.</param>
    public void Use(IReadOnlyList<StashEntry> stashes)
    {
        ArgumentNullException.ThrowIfNull(stashes);

        Entries = stashes;
        _bySha = new Dictionary<string, StashEntry>(StringComparer.Ordinal);

        foreach (StashEntry entry in stashes)
        {
            _bySha.TryAdd(entry.Sha, entry);
        }
    }

    /// <summary>
    /// Tells whether a stash just read is not the one the history was drawn with.
    /// </summary>
    /// <param name="stashes">The entries just read.</param>
    /// <returns>Whether an entry came, went or moved.</returns>
    public bool HasMoved(IReadOnlyList<StashEntry> stashes)
    {
        ArgumentNullException.ThrowIfNull(stashes);

        return !stashes.Select(entry => entry.Sha).SequenceEqual(Entries.Select(entry => entry.Sha), StringComparer.Ordinal);
    }

    /// <summary>
    /// Forgets the helper commits found so far: the history is read again from the top.
    /// </summary>
    public void ForgetHelpers() => _helpers.Clear();

    /// <summary>
    /// Gets the stash entry recorded as a commit, if it is one.
    /// </summary>
    /// <param name="sha">The commit.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public StashEntry? EntryAt(string sha) => _bySha.GetValueOrDefault(sha);

    /// <summary>
    /// A page's commits as the graph draws them: each stash entry is one commit off the commit it was
    /// made on, and the commits git records it with besides — its index, its untracked files — are
    /// not lines of their own.
    /// </summary>
    /// <param name="commits">The page's commits, as git read them.</param>
    /// <param name="decorations">The references, which keep a helper commit one points at.</param>
    /// <returns>The commits to draw.</returns>
    /// <remarks>
    /// git records a stash as a merge of the commit it was made on, a commit of the index and, with
    /// untracked files, a root commit holding them. The log always reads a commit before its parents,
    /// so an entry is met before those commits are, whichever page they fall on. One that a reference
    /// happens to point at is kept: that reference's badge has to be drawn somewhere.
    /// </remarks>
    public IReadOnlyList<GitCommit> Fold(IReadOnlyList<GitCommit> commits, RefDecorationIndex decorations)
    {
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(decorations);

        if (_bySha.Count == 0 && _helpers.Count == 0)
        {
            return commits;
        }

        List<GitCommit> folded = new(commits.Count);

        foreach (GitCommit commit in commits)
        {
            if (_bySha.ContainsKey(commit.Sha))
            {
                for (int parent = 1; parent < commit.ParentShas.Count; parent++)
                {
                    _helpers.Add(commit.ParentShas[parent]);
                }

                folded.Add(commit.ParentShas.Count > 1
                    ? new GitCommit(commit.Sha, [commit.ParentShas[0]], commit.Author, commit.Committer, commit.Subject, commit.Body)
                    : commit);

                continue;
            }

            if (_helpers.Contains(commit.Sha) && decorations.GetRefs(commit.Sha).Count == 0)
            {
                continue;
            }

            folded.Add(commit);
        }

        return folded;
    }

    /// <summary>
    /// A row's badges without <c>refs/stash</c>: every stash line carries a badge naming its own entry,
    /// the newest included.
    /// </summary>
    /// <param name="refs">The references at a commit.</param>
    /// <returns>Them, without the stash's.</returns>
    public static IReadOnlyList<GitRef> WithoutStashRef(IReadOnlyList<GitRef> refs)
    {
        ArgumentNullException.ThrowIfNull(refs);

        foreach (GitRef reference in refs)
        {
            if (reference.Kind == GitRefKind.Stash)
            {
                return [.. refs.Where(candidate => candidate.Kind != GitRefKind.Stash)];
            }
        }

        return refs;
    }
}
