using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Files;

/// <summary>
/// How the changed-files tree is shaped.
/// </summary>
public sealed record FileTreeOptions
{
    /// <summary>
    /// The options the changed-files panel uses.
    /// </summary>
    public static readonly FileTreeOptions Default = new();

    /// <summary>
    /// Gets a value indicating whether a chain of directories with a single child each is shown as
    /// one node, the way GitHub and GitKraken do: <c>src/Enigma/Core</c> rather than three nested
    /// rows a user has to expand one at a time.
    /// </summary>
    public bool CollapseSingleChildDirectories { get; init; } = true;
}

/// <summary>
/// Turns a flat list of changed files into the tree the panel renders when the user picks the tree
/// view. The same list feeds the list view unchanged, which is what makes the toggle free.
/// </summary>
public static class FileTreeBuilder
{
    /// <summary>
    /// Builds the tree.
    /// </summary>
    /// <param name="files">The changed files, in any order.</param>
    /// <param name="options">How the tree is shaped.</param>
    /// <returns>
    /// The root nodes: directories first, then files, each group ordered case-insensitively by name.
    /// </returns>
    public static IReadOnlyList<FileTreeNode> Build(
        IEnumerable<ChangedFile> files,
        FileTreeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        FileTreeOptions effective = options ?? FileTreeOptions.Default;
        MutableNode root = new(string.Empty, string.Empty, isDirectory: true);

        foreach (ChangedFile file in files)
        {
            Insert(root, file);
        }

        List<FileTreeNode> nodes = [];
        foreach (MutableNode child in SortedChildren(root))
        {
            nodes.Add(Freeze(child, effective));
        }

        return nodes;
    }

    /// <summary>
    /// Flattens a built tree back into the files it was made from, in tree order. This is what the
    /// panel uses to keep the list and the tree views in the same order.
    /// </summary>
    /// <param name="nodes">The root nodes.</param>
    /// <returns>The changed files, in tree order.</returns>
    public static IReadOnlyList<ChangedFile> Flatten(IEnumerable<FileTreeNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        List<ChangedFile> files = [];

        foreach (FileTreeNode node in nodes)
        {
            foreach (FileTreeNode descendant in node.DescendantsAndSelf())
            {
                if (descendant.Change is not null)
                {
                    files.Add(descendant.Change);
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Totals the counts of a whole tree.
    /// </summary>
    /// <param name="nodes">The root nodes.</param>
    /// <returns>The combined counts.</returns>
    public static FileTreeCounts Total(IEnumerable<FileTreeNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        FileTreeCounts total = default;
        foreach (FileTreeNode node in nodes)
        {
            total += node.Counts;
        }

        return total;
    }

    private static void Insert(MutableNode root, ChangedFile file)
    {
        string path = ChangedFile.NormalisePath(file.Path);

        if (path.Length == 0)
        {
            return;
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        MutableNode current = root;

        for (int index = 0; index < segments.Length - 1; index++)
        {
            current = current.GetOrAddDirectory(segments[index]);
        }

        current.AddFile(segments[^1], file with { Path = path });
    }

    private static FileTreeNode Freeze(MutableNode node, FileTreeOptions options)
    {
        if (!node.IsDirectory)
        {
            ChangedFile change = node.Change!;
            return new FileTreeNode(node.Name, node.FullPath, false, [], change, FileTreeCounts.ForFile(change));
        }

        MutableNode effective = node;
        string displayName = node.Name;

        if (options.CollapseSingleChildDirectories)
        {
            // Walk down while this directory has exactly one child and that child is a directory,
            // folding the names together as we go.
            while (effective.Children.Count == 1)
            {
                MutableNode only = FirstChild(effective);
                if (!only.IsDirectory)
                {
                    break;
                }

                displayName = $"{displayName}/{only.Name}";
                effective = only;
            }
        }

        List<FileTreeNode> children = [];
        FileTreeCounts counts = default;

        foreach (MutableNode child in SortedChildren(effective))
        {
            FileTreeNode frozen = Freeze(child, options);
            children.Add(frozen);
            counts += frozen.Counts;
        }

        return new FileTreeNode(displayName, effective.FullPath, true, children, null, counts);
    }

    private static MutableNode FirstChild(MutableNode node)
    {
        foreach (MutableNode child in node.Children.Values)
        {
            return child;
        }

        throw new InvalidOperationException("The node has no children.");
    }

    private static List<MutableNode> SortedChildren(MutableNode node)
    {
        List<MutableNode> children = [.. node.Children.Values];

        children.Sort(static (left, right) =>
        {
            // Directories first: that is what makes a tree scannable, and it is what every file
            // browser does.
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            int byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

            // Ordinal as the tie-break, so two names differing only in case still have a stable order.
            return byName != 0 ? byName : string.CompareOrdinal(left.Name, right.Name);
        });

        return children;
    }

    /// <summary>
    /// The mutable shape used while building. It is never handed out: the public tree is immutable.
    /// </summary>
    private sealed class MutableNode
    {
        public MutableNode(string name, string fullPath, bool isDirectory, ChangedFile? change = null)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            Change = change;
        }

        public string Name { get; }

        public string FullPath { get; }

        public bool IsDirectory { get; }

        public ChangedFile? Change { get; private set; }

        public Dictionary<string, MutableNode> Children { get; } = new(StringComparer.Ordinal);

        public MutableNode GetOrAddDirectory(string name)
        {
            if (Children.TryGetValue(name, out MutableNode? existing) && existing.IsDirectory)
            {
                return existing;
            }

            MutableNode directory = new(name, Combine(FullPath, name), isDirectory: true);
            Children[name] = directory;
            return directory;
        }

        public void AddFile(string name, ChangedFile change)
        {
            string fullPath = Combine(FullPath, name);

            if (Children.TryGetValue(name, out MutableNode? existing) && !existing.IsDirectory)
            {
                // The same path appearing twice (a staged and an unstaged change, for instance)
                // keeps the last one rather than duplicating the row.
                existing.Change = change;
                return;
            }

            Children[name] = new MutableNode(name, fullPath, isDirectory: false, change);
        }

        private static string Combine(string parent, string name)
            => parent.Length == 0 ? name : $"{parent}/{name}";
    }
}
