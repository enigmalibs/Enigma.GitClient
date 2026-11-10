using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// One submodule, as the Submodules dialog shows it: its folder, the commit the repository expects it
/// on, and where it stands against that commit.
/// </summary>
/// <remarks>
/// The row carries the page's commands rather than raising events, so a context menu opening in its own
/// popup tree can still reach them with a plain binding against the row itself.
/// </remarks>
public sealed class SubmoduleRowViewModel : ViewModelBase
{
    /// <summary>How far a nested submodule's line is indented per level.</summary>
    public const double IndentPerLevel = 20;

    private readonly SubmodulesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="submodule">The submodule it stands for.</param>
    /// <param name="depth">0 for a submodule of the repository, 1 for one nested in it, and so on.</param>
    public SubmoduleRowViewModel(SubmodulesPageViewModel owner, GitSubmodule submodule, int depth)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(submodule);

        _owner = owner;
        Submodule = submodule;
        Depth = depth;
    }

    /// <summary>Gets the submodule this row stands for.</summary>
    public GitSubmodule Submodule { get; }

    /// <summary>Gets how deep the submodule is nested: 0 for one of the repository's own.</summary>
    public int Depth { get; }

    /// <summary>Gets the line's indent, which puts a nested submodule under the one it is in.</summary>
    public Thickness Indent => new(Depth * IndentPerLevel, 0, 0, 0);

    /// <summary>Gets the submodule's path, from the repository.</summary>
    public string Path => Submodule.Path;

    /// <summary>
    /// Gets what the line shows: the path from the submodule it is nested in, or from the repository.
    /// </summary>
    public string DisplayPath
        => Submodule.ParentPath is { } parent && Submodule.Path.StartsWith(parent + "/", StringComparison.Ordinal)
            ? Submodule.Path[(parent.Length + 1)..]
            : Submodule.Path;

    /// <summary>Gets the expected commit, abbreviated.</summary>
    public string ExpectedShort => Short(Submodule.ExpectedSha);

    /// <summary>Gets the state in words.</summary>
    public string State
        => Submodule.State switch
        {
            SubmoduleCheckoutState.UpToDate => "Up to date",
            SubmoduleCheckoutState.Behind => "Behind",
            SubmoduleCheckoutState.NewCommits => "New commits",
            SubmoduleCheckoutState.Elsewhere => $"Elsewhere, on {Short(Submodule.CheckedOutSha)}",
            SubmoduleCheckoutState.NotDownloaded => "Not downloaded",
            SubmoduleCheckoutState.Conflicted => "Conflicted",
            _ => string.Empty,
        };

    /// <summary>Gets what the state means, for its tooltip.</summary>
    public string StateTip
        => Submodule.State switch
        {
            SubmoduleCheckoutState.UpToDate => "On the commit this repository expects.",
            SubmoduleCheckoutState.Behind =>
                $"On {Short(Submodule.CheckedOutSha)}, older than the commit this repository expects. Update it, or a commit here would record the old one again.",
            SubmoduleCheckoutState.NewCommits =>
                $"On {Short(Submodule.CheckedOutSha)}, newer than the commit this repository expects: work not recorded here yet.",
            SubmoduleCheckoutState.Elsewhere =>
                $"On {Short(Submodule.CheckedOutSha)}, a commit on another line than the one this repository expects.",
            SubmoduleCheckoutState.NotDownloaded => "Its folder is empty. Update it to download it.",
            SubmoduleCheckoutState.Conflicted => "A merge left two commits for it. Resolve the conflict first.",
            _ => string.Empty,
        };

    /// <summary>Gets a value indicating whether the state is one to look at: behind, not downloaded or conflicted.</summary>
    public bool IsAttention
        => Submodule.State is SubmoduleCheckoutState.Behind or SubmoduleCheckoutState.NotDownloaded or SubmoduleCheckoutState.Conflicted;

    /// <summary>Gets a value indicating whether files git tracks in the submodule are modified.</summary>
    public bool HasUncommittedChanges => Submodule.HasUncommittedChanges;

    /// <summary>Gets what the "uncommitted changes" mark says when the pointer rests on it.</summary>
    public string UncommittedTip => "Files in this submodule are modified. An update leaves it where it is.";

    /// <summary>Gets the line's tooltip: the submodule's name and address.</summary>
    public string Details
        => Submodule.Url.Length > 0
            ? $"{Submodule.Name}{Environment.NewLine}{Submodule.Url}"
            : Submodule.Name;

    /// <summary>Gets a value indicating whether the submodule can be opened: it is downloaded.</summary>
    public bool CanOpen => Submodule.IsDownloaded;

    /// <summary>Gets what a greyed Open item says when the pointer rests on it.</summary>
    public string OpenTip => CanOpen ? string.Empty : "Not downloaded yet — update it first";

    /// <summary>Gets a value indicating whether the submodule can be updated from here.</summary>
    public bool CanUpdate => Submodule.State != SubmoduleCheckoutState.Conflicted;

    /// <summary>Gets why <em>Update</em> is greyed, or an empty string when it is not.</summary>
    public string UpdateTip => CanUpdate ? string.Empty : "Resolve its merge conflict first";

    /// <summary>Gets the command that opens the submodule in a new window — a double-click does too.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> OpenCommand => _owner.OpenCommand;

    /// <summary>Gets the command that puts the submodule on the commit the repository expects.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> UpdateCommand => _owner.UpdateCommand;

    /// <summary>Gets the command that opens the submodule's folder in the file manager.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> OpenFolderCommand => _owner.OpenFolderCommand;

    /// <summary>Gets the command that opens a terminal in the submodule.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> OpenTerminalCommand => _owner.OpenTerminalCommand;

    /// <summary>Gets the command that copies the submodule's full path.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> CopyPathCommand => _owner.CopyPathCommand;

    /// <inheritdoc />
    public override string ToString() => Path;

    private static string Short(string? sha) => sha is { Length: > 7 } ? sha[..7] : sha ?? string.Empty;
}

/// <summary>
/// ViewModel behind the Submodules dialog: every submodule of the repository, each followed by the ones
/// nested in it, with where it stands and what can be done with it.
/// </summary>
/// <remarks>
/// Not read with the references on every refresh, as the worktrees are: reading a submodule's state
/// starts git in it, so the list is read when the dialog appears and again after each refresh while it
/// is on screen.
/// </remarks>
public sealed class SubmodulesPageViewModel : PageViewModelBase
{
    private readonly ISubmoduleService _submodules;
    private readonly ISubmoduleOperations _operations;
    private readonly ILogger<SubmodulesPageViewModel> _logger;

    private bool _isShown;
    private int _generation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="submodules">Lists the submodules.</param>
    /// <param name="operations">Updates and opens them.</param>
    /// <param name="logger">Receives a list that could not be read.</param>
    public SubmodulesPageViewModel(
        IRepositoryContext repositoryContext,
        ISubmoduleService submodules,
        ISubmoduleOperations operations,
        ILogger<SubmodulesPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(submodules);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(logger);

        _submodules = submodules;
        _operations = operations;
        _logger = logger;

        OpenCommand = new AsyncRelayCommand<SubmoduleRowViewModel>(row => _operations.OpenAsync(row!.Submodule), row => row?.CanOpen == true);
        UpdateCommand = new AsyncRelayCommand<SubmoduleRowViewModel>(row => _operations.UpdateAsync([row!.Path]), row => row?.CanUpdate == true);
        OpenFolderCommand = new AsyncRelayCommand<SubmoduleRowViewModel>(row => _operations.OpenFolderAsync(row!.Submodule), row => row is not null);
        OpenTerminalCommand = new AsyncRelayCommand<SubmoduleRowViewModel>(row => _operations.OpenTerminalAsync(row!.Submodule), row => row?.CanOpen == true);
        CopyPathCommand = new AsyncRelayCommand<SubmoduleRowViewModel>(row => _operations.CopyPathAsync(row!.Submodule), row => row is not null);
        UpdateAllCommand = new AsyncRelayCommand(() => _operations.UpdateAsync(null), () => Rows.Count > 0);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Submodules";

    /// <summary>Gets every submodule, each followed by the ones nested in it.</summary>
    public ObservableCollection<SubmoduleRowViewModel> Rows { get; } = [];

    /// <summary>Gets a value indicating whether there is nothing to list.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Gets why the list could not be read, or an empty string when it could.</summary>
    public string LoadError { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets the sentence shown while there is nothing to list.</summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to see its submodules."
            : LoadError.Length > 0
                ? $"The submodules could not be read: {LoadError}"
                : "This repository has no submodules.";

    /// <summary>Gets the command that opens a submodule in a new window.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> OpenCommand { get; }

    /// <summary>Gets the command that puts one submodule — and the ones in it — on the expected commits.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> UpdateCommand { get; }

    /// <summary>Gets the command that opens a submodule's folder in the file manager.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> OpenFolderCommand { get; }

    /// <summary>Gets the command that opens a terminal in a submodule.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> OpenTerminalCommand { get; }

    /// <summary>Gets the command that copies a submodule's full path.</summary>
    public AsyncRelayCommand<SubmoduleRowViewModel> CopyPathCommand { get; }

    /// <summary>Gets the command that puts every submodule on the commit the repository expects.</summary>
    public AsyncRelayCommand UpdateAllCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);

        _isShown = true;
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override Task<bool> OnDisappearingAsync()
    {
        _isShown = false;
        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Reads the submodules again and rebuilds the rows; a read overtaken by a newer one is dropped.
    /// </summary>
    /// <returns>A task that completes once the rows are rebuilt.</returns>
    public async Task ReloadAsync()
    {
        int generation = ++_generation;

        if (RepositoryContext.Repository is not { } repository)
        {
            Show([], string.Empty);
            return;
        }

        try
        {
            IReadOnlyList<GitSubmodule> listed = await _submodules
                .ListAsync(repository, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            if (generation == _generation && ReferenceEquals(repository, RepositoryContext.Repository))
            {
                Show(listed, string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // The repository was swapped or closed; its successor reads its own list.
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "The submodules of {Repository} could not be read", repository);

            if (generation == _generation)
            {
                Show([], ArgumentRedactor.Redact(exception.StandardError.Trim()));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        if (_isShown)
        {
            ReloadAsync().Forget();
        }
        else
        {
            Show([], string.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed()
    {
        if (_isShown)
        {
            ReloadAsync().Forget();
        }
    }

    private void Show(IReadOnlyList<GitSubmodule> submodules, string error)
    {
        Rows.Clear();

        Dictionary<string, int> depths = new(StringComparer.Ordinal);

        foreach (GitSubmodule submodule in submodules)
        {
            int depth = submodule.ParentPath is { } parent && depths.TryGetValue(parent, out int parentDepth) ? parentDepth + 1 : 0;
            depths[submodule.Path] = depth;

            Rows.Add(new SubmoduleRowViewModel(this, submodule, depth));
        }

        LoadError = error;

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        UpdateAllCommand.NotifyCanExecuteChanged();
    }
}
