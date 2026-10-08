using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Enigma.GitClient.Core.Configuration;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// A line of the branches tree: one of its top-level nodes (the local branches, or one remote's), a
/// folder, or a branch.
/// </summary>
public abstract class BranchTreeNode : ViewModelBase
{
    private Action<BranchTreeNode>? _expansionChanged;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="key">The node's identity across rebuilds.</param>
    protected BranchTreeNode(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Key = key;
    }

    /// <summary>Gets what the node's line says.</summary>
    public abstract string Label { get; }

    /// <summary>
    /// Gets the node's identity across rebuilds — the page rebuilds the tree on every refresh and every
    /// keystroke in the filter, and remembers which nodes are open by this.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets a value indicating whether the node holds other nodes — a top-level node or a folder — and
    /// is never part of the selection, rather than being a branch.
    /// </summary>
    public abstract bool IsFolder { get; }

    /// <summary>Gets the nodes under this one: folders first, then branches.</summary>
    public virtual IReadOnlyList<BranchTreeNode> Children => [];

    /// <summary>Gets or sets a value indicating whether the node is open in the tree.</summary>
    public bool IsExpanded
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _expansionChanged?.Invoke(this);
            }
        }
    }

    /// <summary>
    /// Opens or closes the node as the tree is built — what the page remembered, which it need not be
    /// told back — and from then on tells the page when the reader opens or closes it.
    /// </summary>
    /// <param name="expanded">Whether the node starts open.</param>
    /// <param name="expansionChanged">Told when the reader opens or closes the node.</param>
    internal void Restore(bool expanded, Action<BranchTreeNode>? expansionChanged)
    {
        IsExpanded = expanded;
        _expansionChanged = expansionChanged;
    }
}

/// <summary>
/// A folder of the branches tree: a <c>/</c> segment the branch names under it share — or, for a chain
/// of folders that each hold a single folder, the whole chain on one line.
/// </summary>
public class BranchFolderViewModel : BranchTreeNode
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="key">The folder's identity across rebuilds.</param>
    /// <param name="label">What its line says: its name, or a merged chain's names joined by <c>/</c>.</param>
    /// <param name="children">The nodes under it, in order.</param>
    public BranchFolderViewModel(string key, string label, IReadOnlyList<BranchTreeNode> children)
        : base(key)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(children);

        Label = label;
        Children = children;
        Rows = [.. Branches(children)];
    }

    /// <inheritdoc />
    public override string Label { get; }

    /// <inheritdoc />
    public override bool IsFolder => true;

    /// <inheritdoc />
    public override IReadOnlyList<BranchTreeNode> Children { get; }

    /// <summary>Gets every branch under the node, at any depth, in the order the tree shows them.</summary>
    public IReadOnlyList<BranchRowViewModel> Rows { get; }

    /// <summary>Gets how many branches are under the node, as its line shows it.</summary>
    public string Count => Rows.Count.ToString(CultureInfo.CurrentCulture);

    private static IEnumerable<BranchRowViewModel> Branches(IEnumerable<BranchTreeNode> nodes)
    {
        foreach (BranchTreeNode node in nodes)
        {
            if (node is BranchRowViewModel row)
            {
                yield return row;
            }
            else
            {
                foreach (BranchRowViewModel nested in Branches(node.Children))
                {
                    yield return nested;
                }
            }
        }
    }
}

/// <summary>
/// A top-level node of the branches tree: the local branches, or one remote's.
/// </summary>
public sealed class BranchGroupViewModel : BranchFolderViewModel
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="title">"Local", or the remote's name.</param>
    /// <param name="isRemote">Whether the node holds a remote's branches.</param>
    /// <param name="children">The nodes under it, in order.</param>
    public BranchGroupViewModel(string title, bool isRemote, IReadOnlyList<BranchTreeNode> children)
        : base(KeyOf(title, isRemote), title, children)
    {
        IsRemote = isRemote;
    }

    /// <summary>Gets what the node is called: "Local", or the remote's name.</summary>
    public string Title => Label;

    /// <summary>Gets a value indicating whether the node holds a remote's branches.</summary>
    public bool IsRemote { get; }

    /// <summary>
    /// The key of a top-level node: one for the local branches, and one per remote — a remote called
    /// "local" included.
    /// </summary>
    /// <param name="title">"Local", or the remote's name.</param>
    /// <param name="isRemote">Whether the node holds a remote's branches.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(string title, bool isRemote) => isRemote ? $"remote:{title}" : "local";
}

/// <summary>
/// Builds the branches tree from the branches' lines.
/// </summary>
/// <remarks>
/// <para>
/// "Local" first, then one node per remote, alphabetical. Inside each, every <c>/</c> segment of a
/// branch's name is a folder, and a chain of folders that each hold a single folder is one line —
/// <c>vibe/2026-10-08</c> — as <c>FileTreeBuilder</c> shows the changed files. Folders come first,
/// by name; branches after, in the page's order and direction.
/// </para>
/// <para>
/// A top-level node starts open and a folder closed, unless the page remembered otherwise; while the
/// filter narrows the tree, every node is open, so every match shows with the folders above it.
/// </para>
/// </remarks>
public static class BranchTreeBuilder
{
    /// <summary>
    /// Builds the tree.
    /// </summary>
    /// <param name="local">The local branches' lines, already filtered.</param>
    /// <param name="remote">The remote branches' lines, already filtered.</param>
    /// <param name="key">What the branches are ordered by.</param>
    /// <param name="direction">Which way.</param>
    /// <param name="remembered">
    /// Whether the reader left a node open, by its key, or <see langword="null"/> when the node has no
    /// remembered state.
    /// </param>
    /// <param name="expandAll">Whether every node is open, as it is while the filter narrows the tree.</param>
    /// <param name="expansionChanged">Told when the reader opens or closes a node.</param>
    /// <returns>The top-level nodes, in order.</returns>
    public static IReadOnlyList<BranchGroupViewModel> Build(
        IReadOnlyList<BranchRowViewModel> local,
        IReadOnlyList<BranchRowViewModel> remote,
        RefSortKey key,
        SortDirection direction,
        Func<string, bool?> remembered,
        bool expandAll,
        Action<BranchTreeNode>? expansionChanged)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(remembered);

        List<BranchGroupViewModel> groups = [];

        if (local.Count > 0)
        {
            groups.Add(Group("Local", isRemote: false, local));
        }

        IEnumerable<IGrouping<string, BranchRowViewModel>> remotes = remote
            .GroupBy(row => row.Branch.RemoteName ?? "remote", StringComparer.Ordinal)
            .OrderBy(byRemote => byRemote.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(byRemote => byRemote.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, BranchRowViewModel> byRemote in remotes)
        {
            groups.Add(Group(byRemote.Key, isRemote: true, [.. byRemote]));
        }

        return groups;

        BranchGroupViewModel Group(string title, bool isRemote, IReadOnlyList<BranchRowViewModel> rows)
        {
            string groupKey = BranchGroupViewModel.KeyOf(title, isRemote);
            Folder root = new();

            foreach (BranchRowViewModel row in rows)
            {
                string[] segments = row.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                Folder folder = root;

                for (int index = 0; index < segments.Length - 1; index++)
                {
                    folder = folder.Child(segments[index]);
                }

                folder.Branches.Add(row);
            }

            BranchGroupViewModel group = new(title, isRemote, Freeze(root, groupKey, string.Empty));
            group.Restore(expandAll || (remembered(group.Key) ?? true), expansionChanged);

            return group;
        }

        List<BranchTreeNode> Freeze(Folder folder, string groupKey, string path)
        {
            List<BranchTreeNode> nodes = [];

            foreach ((string name, Folder child) in folder.Folders
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Folder effective = child;
                string label = name;
                string effectivePath = path.Length == 0 ? name : $"{path}/{name}";

                // A folder holding nothing but one folder is the same line as that folder.
                while (effective.Branches.Count == 0 && effective.Folders.Count == 1)
                {
                    (string onlyName, Folder only) = effective.Folders.Single();

                    label = $"{label}/{onlyName}";
                    effectivePath = $"{effectivePath}/{onlyName}";
                    effective = only;
                }

                BranchFolderViewModel node = new($"{groupKey}/{effectivePath}", label, Freeze(effective, groupKey, effectivePath));
                node.Restore(expandAll || (remembered(node.Key) ?? false), expansionChanged);

                nodes.Add(node);
            }

            nodes.AddRange(RefSort.Order(folder.Branches, row => row.Name, row => row.Branch.TipDate, key, direction));

            return nodes;
        }
    }

    /// <summary>The mutable shape used while building; never handed out.</summary>
    private sealed class Folder
    {
        public Dictionary<string, Folder> Folders { get; } = new(StringComparer.Ordinal);

        public List<BranchRowViewModel> Branches { get; } = [];

        public Folder Child(string name)
        {
            if (!Folders.TryGetValue(name, out Folder? child))
            {
                child = new Folder();
                Folders[name] = child;
            }

            return child;
        }
    }
}
