using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Files;

/// <summary>
/// How many changes of each kind sit beneath a node, which is what a folder's badge shows.
/// </summary>
/// <param name="Added">Files added.</param>
/// <param name="Modified">Files modified in place.</param>
/// <param name="Deleted">Files deleted.</param>
/// <param name="Renamed">Files renamed or copied.</param>
/// <param name="Conflicted">Files with unresolved conflicts.</param>
/// <param name="AddedLines">Lines added across the files.</param>
/// <param name="RemovedLines">Lines removed across the files.</param>
public readonly record struct FileTreeCounts(
    int Added,
    int Modified,
    int Deleted,
    int Renamed,
    int Conflicted,
    int AddedLines,
    int RemovedLines)
{
    /// <summary>
    /// Gets how many files the node covers.
    /// </summary>
    public int Total => Added + Modified + Deleted + Renamed;

    /// <summary>
    /// Adds two sets of counts together.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The combined counts.</returns>
    public static FileTreeCounts operator +(FileTreeCounts left, FileTreeCounts right)
        => new(
            left.Added + right.Added,
            left.Modified + right.Modified,
            left.Deleted + right.Deleted,
            left.Renamed + right.Renamed,
            left.Conflicted + right.Conflicted,
            left.AddedLines + right.AddedLines,
            left.RemovedLines + right.RemovedLines);

    /// <summary>
    /// Adds two sets of counts together.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The combined counts.</returns>
    public static FileTreeCounts Add(FileTreeCounts left, FileTreeCounts right) => left + right;

    /// <summary>
    /// Counts a single file.
    /// </summary>
    /// <param name="file">The changed file.</param>
    /// <returns>The counts for that one file.</returns>
    public static FileTreeCounts ForFile(ChangedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        int added = 0;
        int modified = 0;
        int deleted = 0;
        int renamed = 0;

        switch (file.ChangeKind)
        {
            case Diff.FileChangeKind.Added:
                added = 1;
                break;
            case Diff.FileChangeKind.Deleted:
                deleted = 1;
                break;
            case Diff.FileChangeKind.Renamed:
            case Diff.FileChangeKind.Copied:
                renamed = 1;
                break;
            default:
                modified = 1;
                break;
        }

        return new FileTreeCounts(
            added,
            modified,
            deleted,
            renamed,
            file.IsConflicted ? 1 : 0,
            file.AddedLines,
            file.RemovedLines);
    }
}

/// <summary>
/// One node of the changed-files tree: either a directory with children, or a file carrying its
/// change.
/// </summary>
public sealed class FileTreeNode
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="name">
    /// The label shown to the user. For a collapsed chain of single-child directories this is the
    /// whole chain, for example <c>src/Enigma/Core</c>.
    /// </param>
    /// <param name="fullPath">The node's full path from the repository root.</param>
    /// <param name="isDirectory">Whether the node is a directory.</param>
    /// <param name="children">The node's children, already sorted.</param>
    /// <param name="change">The file's change, or <see langword="null"/> for a directory.</param>
    /// <param name="counts">The counts for this node and everything beneath it.</param>
    public FileTreeNode(
        string name,
        string fullPath,
        bool isDirectory,
        IReadOnlyList<FileTreeNode> children,
        ChangedFile? change,
        FileTreeCounts counts)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(children);

        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Children = children;
        Change = change;
        Counts = counts;
    }

    /// <summary>
    /// Gets the label shown to the user, which for a collapsed directory chain spans several path
    /// segments.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the node's full path from the repository root.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Gets a value indicating whether the node is a directory.
    /// </summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Gets the node's children, directories first and then case-insensitively by name.
    /// </summary>
    public IReadOnlyList<FileTreeNode> Children { get; }

    /// <summary>
    /// Gets the file's change, or <see langword="null"/> for a directory.
    /// </summary>
    public ChangedFile? Change { get; }

    /// <summary>
    /// Gets the counts for this node and everything beneath it.
    /// </summary>
    public FileTreeCounts Counts { get; }

    /// <summary>
    /// Enumerates this node and every node beneath it, depth first.
    /// </summary>
    /// <returns>The node and its descendants.</returns>
    public IEnumerable<FileTreeNode> DescendantsAndSelf()
    {
        yield return this;

        foreach (FileTreeNode child in Children)
        {
            foreach (FileTreeNode descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Finds the node with a given full path, anywhere beneath this one.
    /// </summary>
    /// <param name="fullPath">The full path to look for.</param>
    /// <returns>The node, or <see langword="null"/> when it is not there.</returns>
    public FileTreeNode? Find(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        foreach (FileTreeNode node in DescendantsAndSelf())
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public override string ToString()
        => IsDirectory ? $"{Name}/ ({Counts.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : Name;
}
