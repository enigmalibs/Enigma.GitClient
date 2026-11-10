using System;
using System.Collections.Generic;
using System.Globalization;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// Which way a change of a submodule's recorded commit goes.
/// </summary>
public enum PinDirection
{
    /// <summary>To a newer commit: it brings commits in.</summary>
    Forward,

    /// <summary>To an older commit: it takes commits back out.</summary>
    Back,

    /// <summary>To a commit on another line: it brings some in and takes some back.</summary>
    Diverged,

    /// <summary>
    /// Not known here: the submodule is not downloaded, lacks one of the commits, or appears or
    /// disappears in the change.
    /// </summary>
    Unknown,

    /// <summary>The same commit on both sides.</summary>
    Unchanged,
}

/// <summary>
/// One commit a pin change brings in or takes back.
/// </summary>
/// <param name="ShortSha">The commit, abbreviated.</param>
/// <param name="Title">Its message's first line.</param>
/// <param name="Author">Its author's name.</param>
/// <param name="Date">When it was written.</param>
public sealed record PinCommit(string ShortSha, string Title, string Author, DateTimeOffset Date)
{
    /// <summary>
    /// Gets the commit's line in a pin change: "f00ba12  Fix X — Ada, 2026-11-01".
    /// </summary>
    public string Line => $"{ShortSha}  {Title} — {Author}, {Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// What a change of a submodule's recorded commit does: which way it goes, and the commits it brings in
/// or takes back.
/// </summary>
/// <param name="Path">The submodule's path.</param>
/// <param name="OldSha">The commit recorded before, empty when the submodule is new.</param>
/// <param name="NewSha">The commit recorded after, empty when the submodule is gone.</param>
/// <param name="Direction">Which way it goes.</param>
/// <param name="Added">The commits it brings in, newest first, at most <see cref="SubmoduleService.PinCommitsShown"/>.</param>
/// <param name="AddedCount">How many it brings in in all.</param>
/// <param name="Removed">The commits it takes back, newest first, at most <see cref="SubmoduleService.PinCommitsShown"/>.</param>
/// <param name="RemovedCount">How many it takes back in all.</param>
/// <param name="UnknownReason">Why <see cref="PinDirection.Unknown"/>, or an empty string.</param>
public sealed record PinChange(
    string Path,
    string OldSha,
    string NewSha,
    PinDirection Direction,
    IReadOnlyList<PinCommit> Added,
    int AddedCount,
    IReadOnlyList<PinCommit> Removed,
    int RemovedCount,
    string UnknownReason = "")
{
    /// <summary>
    /// Gets the one line that says it: "libs/Common: a1b2c3d → f00ba12 · 3 new commits".
    /// </summary>
    public string Summary
    {
        get
        {
            string move = OldSha.Length == 0
                ? $"{Path}: added at {Short(NewSha)}"
                : NewSha.Length == 0
                    ? $"{Path}: removed (was {Short(OldSha)})"
                    : Direction == PinDirection.Unchanged
                        ? $"{Path}: {Short(NewSha)}"
                        : $"{Path}: {Short(OldSha)} → {Short(NewSha)}";

            return Direction switch
            {
                PinDirection.Forward => $"{move} · {Commits(AddedCount, "new commit", "new commits")}",
                PinDirection.Back => $"{move} · goes back {Commits(RemovedCount, "commit", "commits")}",
                PinDirection.Diverged =>
                    $"{move} · {Commits(AddedCount, "new commit", "new commits")}, {Commits(RemovedCount, "commit", "commits")} taken back",
                _ => move,
            };
        }
    }

    /// <summary>
    /// Gets the sentence for the commits beyond the ones listed — "and 12 more" — or an empty string.
    /// </summary>
    /// <param name="listed">How many are listed.</param>
    /// <param name="total">How many there are.</param>
    /// <returns>The sentence.</returns>
    public static string More(int listed, int total)
        => total > listed ? $"and {(total - listed).ToString(CultureInfo.CurrentCulture)} more" : string.Empty;

    /// <summary>
    /// Gets the lines listing the commits: those brought in marked <c>+</c>, those taken back <c>−</c>,
    /// each side followed by "and N more" when it holds more than it lists.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            List<string> lines = [];

            foreach (PinCommit commit in Added)
            {
                lines.Add("+ " + commit.Line);
            }

            if (More(Added.Count, AddedCount) is { Length: > 0 } moreAdded)
            {
                lines.Add("+ " + moreAdded);
            }

            foreach (PinCommit commit in Removed)
            {
                lines.Add("− " + commit.Line);
            }

            if (More(Removed.Count, RemovedCount) is { Length: > 0 } moreRemoved)
            {
                lines.Add("− " + moreRemoved);
            }

            return lines;
        }
    }

    /// <summary>
    /// Says what a submodule's own files hold, for a change whose new side is <c>-dirty</c>.
    /// </summary>
    /// <param name="modified">Whether files git tracks in it are modified.</param>
    /// <param name="untracked">Whether it holds untracked files.</param>
    /// <returns>The sentence, or an empty string when it holds neither.</returns>
    public static string DescribeDirt(bool modified, bool untracked)
        => (modified, untracked) switch
        {
            (true, true) => "The submodule has modified content and untracked files.",
            (true, false) => "The submodule has modified content.",
            (false, true) => "The submodule has untracked files.",
            _ => string.Empty,
        };

    /// <summary>Abbreviates a commit to seven characters.</summary>
    /// <param name="sha">The commit.</param>
    /// <returns>The abbreviation.</returns>
    public static string Short(string? sha) => sha is { Length: > 7 } ? sha[..7] : sha ?? string.Empty;

    private static string Commits(int count, string one, string many)
        => count == 1 ? $"1 {one}" : $"{count.ToString(CultureInfo.CurrentCulture)} {many}";
}

/// <summary>
/// A submodule's recorded commit changed between HEAD and the index — a change a commit would record.
/// </summary>
/// <param name="Path">The submodule's path.</param>
/// <param name="HeadSha">The commit HEAD records, empty for a submodule HEAD does not have.</param>
/// <param name="IndexSha">The commit the index records, empty for one it removes.</param>
public sealed record StagedPin(string Path, string HeadSha, string IndexSha);

/// <summary>
/// What discarding a submodule's change would do: the commit it goes back to, and what stops it or would
/// be lost.
/// </summary>
/// <param name="Path">The submodule's path.</param>
/// <param name="HeadSha">The commit HEAD records, which it goes back to.</param>
/// <param name="CheckedOutSha">The commit it has checked out, or <see langword="null"/> when not downloaded.</param>
/// <param name="HasUncommittedChanges">Whether files in it are modified, which refuses the discard.</param>
/// <param name="CommitsLeftBehind">
/// How many commits from its checkout no branch, tag or remote branch contains: what moving it would leave
/// where nothing finds it again.
/// </param>
public sealed record SubmoduleDiscard(
    string Path,
    string HeadSha,
    string? CheckedOutSha,
    bool HasUncommittedChanges,
    int CommitsLeftBehind)
{
    /// <summary>Gets a value indicating whether the checkout moves: it is not on HEAD's commit.</summary>
    public bool Moves => CheckedOutSha is not null && !string.Equals(CheckedOutSha, HeadSha, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the sentence for the discard's question.</summary>
    public string Describe()
        => CommitsLeftBehind switch
        {
            0 => $"{Path} goes back to {PinChange.Short(HeadSha)}.",
            1 => $"{Path} goes back to {PinChange.Short(HeadSha)}; 1 commit no branch contains is left behind.",
            _ => $"{Path} goes back to {PinChange.Short(HeadSha)}; {CommitsLeftBehind.ToString(CultureInfo.CurrentCulture)} commits no branch contains are left behind.",
        };
}
