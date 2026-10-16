using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// One working tree, as the Worktrees dialog shows it: its folder, what it has checked out, and what
/// state it is in.
/// </summary>
/// <remarks>
/// The row carries the page's commands rather than raising events, so a context menu opening in its own
/// popup tree can still reach them with a plain binding against the row itself.
/// </remarks>
public sealed class WorktreeRowViewModel : ViewModelBase
{
    private readonly WorktreesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="worktree">The working tree it stands for.</param>
    /// <param name="isThisWindow">Whether it is the working tree this window has open.</param>
    public WorktreeRowViewModel(WorktreesPageViewModel owner, GitWorktree worktree, bool isThisWindow)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(worktree);

        _owner = owner;
        Worktree = worktree;
        IsThisWindow = isThisWindow;
    }

    /// <summary>Gets the working tree this row stands for.</summary>
    public GitWorktree Worktree { get; }

    /// <summary>Gets the folder's name, which the line shows; its tooltip is the whole path.</summary>
    public string Name => System.IO.Path.GetFileName(Worktree.Path) is { Length: > 0 } name ? name : Worktree.Path;

    /// <summary>Gets the folder's full path.</summary>
    public string Path => Worktree.Path;

    /// <summary>
    /// Gets what the working tree has checked out: the branch, or "detached at" its commit, or "bare".
    /// </summary>
    public string Checkout
        => Worktree.IsBare
            ? "bare"
            : Worktree.Branch is { Length: > 0 } branch && !Worktree.IsDetached
                ? branch
                : $"detached at {ShortSha(Worktree.HeadSha)}";

    /// <summary>Gets a value indicating whether this is the main working tree.</summary>
    public bool IsMain => Worktree.IsMain;

    /// <summary>Gets a value indicating whether this is the working tree this window has open.</summary>
    public bool IsThisWindow { get; }

    /// <summary>Gets a value indicating whether the worktree is locked.</summary>
    public bool IsLocked => Worktree.IsLocked;

    /// <summary>Gets what the "locked" badge says when the pointer rests on it: why, when git was told.</summary>
    public string LockTip => Worktree.LockReason.Length > 0 ? $"Locked: {Worktree.LockReason}" : "Locked";

    /// <summary>Gets a value indicating whether the worktree's folder is gone.</summary>
    public bool IsMissing => Worktree.IsMissing;

    /// <summary>Gets what the "main" badge says when the pointer rests on it.</summary>
    public string MainTip => "The main working tree, which holds the repository's .git folder";

    /// <summary>Gets what the "this window" badge says when the pointer rests on it.</summary>
    public string ThisWindowTip => "The working tree this window has open";

    /// <summary>Gets what the "folder missing" badge says when the pointer rests on it.</summary>
    public string MissingTip => "Its folder is gone; removing the worktree clears git's record of it";

    /// <summary>Gets a value indicating whether the worktree can be opened: its folder is there.</summary>
    public bool CanOpen => !IsMissing;

    /// <summary>Gets what a greyed Open item says when the pointer rests on it.</summary>
    public string OpenTip => IsMissing ? "Its folder is missing" : string.Empty;

    /// <summary>
    /// Gets a value indicating whether the worktree can be removed from here: a linked one, not the one
    /// this window has open, and not locked.
    /// </summary>
    public bool CanRemove => !IsMain && !IsThisWindow && !IsLocked;

    /// <summary>Gets why <em>Remove…</em> is greyed, or an empty string when it is not.</summary>
    public string RemoveTip
        => IsMain
            ? "The main working tree cannot be removed"
            : IsThisWindow
                ? "Remove it from another window of this repository"
                : IsLocked
                    ? $"{LockTip} — unlock it with git worktree unlock"
                    : string.Empty;

    /// <summary>Gets the command that opens the worktree in a new window — a double-click does too.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> OpenCommand => _owner.OpenCommand;

    /// <summary>Gets the command that opens the worktree's folder in the file manager.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> OpenFolderCommand => _owner.OpenFolderCommand;

    /// <summary>Gets the command that opens a terminal in the worktree.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> OpenTerminalCommand => _owner.OpenTerminalCommand;

    /// <summary>Gets the command that removes the worktree, after saying what it costs.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> RemoveCommand => _owner.RemoveCommand;

    /// <inheritdoc />
    public override string ToString() => Path;

    private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;
}

/// <summary>
/// ViewModel behind the Worktrees dialog: every working tree of the repository, main first, and what
/// can be done with each.
/// </summary>
/// <remarks>
/// The list is the repository context's, read with the references on every refresh, so a worktree made
/// or removed anywhere — this dialog, a branch's menu, a terminal — is here after the next one. Every
/// action belongs to <see cref="IWorktreeOperations"/>, which the branch menus call too.
/// </remarks>
public sealed class WorktreesPageViewModel : PageViewModelBase
{
    private readonly IWorktreeOperations _operations;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="operations">Opens and removes the worktrees.</param>
    public WorktreesPageViewModel(IRepositoryContext repositoryContext, IWorktreeOperations operations)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(operations);

        _operations = operations;

        OpenCommand = new AsyncRelayCommand<WorktreeRowViewModel>(row => _operations.OpenAsync(row!.Worktree), row => row?.CanOpen == true);
        OpenFolderCommand = new AsyncRelayCommand<WorktreeRowViewModel>(row => _operations.OpenFolderAsync(row!.Worktree), row => row?.CanOpen == true);
        OpenTerminalCommand = new AsyncRelayCommand<WorktreeRowViewModel>(row => _operations.OpenTerminalAsync(row!.Worktree), row => row?.CanOpen == true);
        RemoveCommand = new AsyncRelayCommand<WorktreeRowViewModel>(OnRemoveAsync, row => row?.CanRemove == true);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Worktrees";

    /// <summary>Gets every working tree, the main one first, then the others by folder.</summary>
    public ObservableCollection<WorktreeRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets a value indicating whether there is nothing to list but the main working tree — or nothing
    /// at all, while no repository is open.
    /// </summary>
    public bool IsEmpty => Rows.Count(row => !row.IsMain) == 0;

    /// <summary>Gets the sentence shown while there is nothing to list.</summary>
    public string EmptyMessage
        => IsRepositoryOpen
            ? "No other worktrees. Create one from a branch's or a commit's menu."
            : "Open a repository to see its worktrees.";

    /// <summary>Gets the command that opens a worktree in a new window.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> OpenCommand { get; }

    /// <summary>Gets the command that opens a worktree's folder in the file manager.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> OpenFolderCommand { get; }

    /// <summary>Gets the command that opens a terminal in a worktree.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> OpenTerminalCommand { get; }

    /// <summary>Gets the command that removes a worktree, after saying what it costs.</summary>
    public AsyncRelayCommand<WorktreeRowViewModel> RemoveCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => Rebuild();

    /// <summary>
    /// Rebuilds the rows from what the repository context last read.
    /// </summary>
    private void Rebuild()
    {
        Rows.Clear();

        string? open = RepositoryContext.Repository?.WorkTreePath;
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        IEnumerable<GitWorktree> ordered = RepositoryContext.Worktrees
            .OrderBy(worktree => worktree.IsMain ? 0 : 1)
            .ThenBy(worktree => worktree.Path, paths);

        foreach (GitWorktree worktree in ordered)
        {
            Rows.Add(new WorktreeRowViewModel(this, worktree, open is not null && paths.Equals(worktree.Path, open)));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private async Task OnRemoveAsync(WorktreeRowViewModel? row)
    {
        if (row is not null && await _operations.RemoveAsync(row.Worktree).ConfigureAwait(true))
        {
            // The refresh after the removal rebuilt the rows already; this covers a context that
            // could not be read again.
            Rebuild();
        }
    }
}
