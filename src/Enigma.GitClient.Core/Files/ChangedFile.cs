using System;
using Enigma.GitClient.Core.Diff;

namespace Enigma.GitClient.Core.Files;

/// <summary>
/// Where a change lives relative to the index, for the working-directory view.
/// </summary>
public enum FileStagingState
{
    /// <summary>The change comes from a commit, so staging does not apply.</summary>
    NotApplicable,

    /// <summary>The change is in the index and would be part of the next commit.</summary>
    Staged,

    /// <summary>The change is in the work tree only.</summary>
    Unstaged,

    /// <summary>The file has both a staged and a further unstaged change.</summary>
    PartiallyStaged,
}

/// <summary>
/// One file touched by a change, summarised for a list or a tree. It is the shape the changed-files
/// panel binds to, whether the change came from a commit or from the working directory.
/// </summary>
public sealed record ChangedFile
{
    /// <summary>
    /// Gets the file's path after the change, relative to the repository root and always separated
    /// by forward slashes.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the file's path before the change, for a rename or a copy.
    /// </summary>
    public string? OldPath { get; init; }

    /// <summary>Gets what happened to the file.</summary>
    public FileChangeKind ChangeKind { get; init; } = FileChangeKind.Modified;

    /// <summary>Gets where the change lives relative to the index.</summary>
    public FileStagingState Staging { get; init; } = FileStagingState.NotApplicable;

    /// <summary>Gets how many lines the change adds.</summary>
    public int AddedLines { get; init; }

    /// <summary>Gets how many lines the change removes.</summary>
    public int RemovedLines { get; init; }

    /// <summary>Gets a value indicating whether git refused to show a textual diff.</summary>
    public bool IsBinary { get; init; }

    /// <summary>Gets a value indicating whether the file has unresolved conflicts.</summary>
    public bool IsConflicted { get; init; }

    /// <summary>Gets a value indicating whether the entry is a submodule rather than a file.</summary>
    public bool IsSubmodule { get; init; }

    /// <summary>
    /// Gets a value indicating whether git has never been told about the file.
    /// </summary>
    public bool IsUntracked { get; init; }

    /// <summary>
    /// Gets how the index differs from HEAD, or <see langword="null"/> when it does not.
    /// </summary>
    /// <remarks>
    /// A status entry has two independent halves: what is staged, and what is not. A file edited,
    /// staged and edited again differs from HEAD <em>and</em> from the index, and a client that
    /// keeps only one of the two eventually commits something the user did not look at.
    /// </remarks>
    public FileChangeKind? IndexStatus { get; init; }

    /// <summary>
    /// Gets how the work tree differs from the index, or <see langword="null"/> when it does not.
    /// </summary>
    public FileChangeKind? WorkTreeStatus { get; init; }

    /// <summary>
    /// Gets the two-letter conflict state git reported for an unmerged path — <c>UU</c>, <c>AA</c>,
    /// <c>DU</c> and the rest — empty when the file is not conflicted.
    /// </summary>
    public string ConflictState { get; init; } = string.Empty;

    /// <summary>
    /// Gets the submodule's own state, when the entry is one.
    /// </summary>
    public Status.SubmoduleState Submodule { get; init; }

    /// <summary>
    /// Gets the file's name — the last segment of <see cref="Path"/>.
    /// </summary>
    public string Name
    {
        get
        {
            int separator = Path.LastIndexOf('/');
            return separator < 0 ? Path : Path.Substring(separator + 1);
        }
    }

    /// <summary>
    /// Gets the directory the file sits in, empty when it is at the repository root.
    /// </summary>
    public string DirectoryPath
    {
        get
        {
            int separator = Path.LastIndexOf('/');
            return separator < 0 ? string.Empty : Path.Substring(0, separator);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the file moved.
    /// </summary>
    public bool IsRenamed
        => ChangeKind is FileChangeKind.Renamed or FileChangeKind.Copied &&
           OldPath is not null &&
           !string.Equals(OldPath, Path, StringComparison.Ordinal);

    /// <summary>
    /// Normalises a path to the forward-slash form the rest of the client uses.
    /// </summary>
    /// <param name="path">A path in either separator style.</param>
    /// <returns>The normalised path.</returns>
    public static string NormalisePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// Summarises a parsed patch as a changed file.
    /// </summary>
    /// <param name="patch">The file's patch.</param>
    /// <param name="staging">Where the change lives relative to the index.</param>
    /// <returns>The summary.</returns>
    public static ChangedFile FromPatch(FilePatch patch, FileStagingState staging = FileStagingState.NotApplicable)
    {
        ArgumentNullException.ThrowIfNull(patch);

        return new ChangedFile
        {
            Path = NormalisePath(patch.DisplayPath),
            OldPath = patch.OldPath is null ? null : NormalisePath(patch.OldPath),
            ChangeKind = patch.ChangeKind,
            Staging = staging,
            AddedLines = patch.AddedLines,
            RemovedLines = patch.RemovedLines,
            IsBinary = patch.IsBinary,
            IsSubmodule = patch.IsSubmodule,
        };
    }

    /// <inheritdoc />
    public override string ToString() => $"{ChangeKind} {Path}";
}
