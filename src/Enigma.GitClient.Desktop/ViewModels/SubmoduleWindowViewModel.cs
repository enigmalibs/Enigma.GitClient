using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels;

/// <summary>
/// What a window open on a submodule says about it: whose submodule it is, after the repository's
/// name, and — while it is on no branch, as a submodule is after every update — a band offering a branch
/// to commit on.
/// </summary>
/// <remarks>
/// git leaves a submodule on its expected commit with no branch checked out. A commit made there belongs
/// to no branch: the next update moves the submodule away and the commit is left where nothing finds
/// it. The band says so, and offers the branch already at that commit — or a remote one, checked out as
/// a tracking branch — and a new branch there. A window on anything but a submodule never shows it.
/// </remarks>
public sealed class SubmoduleWindowViewModel : ViewModelBase
{
    private readonly IRepositoryContext _context;
    private readonly ISubmoduleService _submodules;
    private readonly IBranchOperations _branches;
    private readonly ILogger<SubmoduleWindowViewModel> _logger;

    private GitBranch? _branchAtHead;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the window is looking at.</param>
    /// <param name="submodules">Finds the repository's superproject.</param>
    /// <param name="branches">Checks a branch out, or creates one.</param>
    /// <param name="logger">Receives a superproject that could not be read.</param>
    public SubmoduleWindowViewModel(
        IRepositoryContext context,
        ISubmoduleService submodules,
        IBranchOperations branches,
        ILogger<SubmoduleWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(submodules);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _submodules = submodules;
        _branches = branches;
        _logger = logger;

        CheckoutBranchCommand = new AsyncRelayCommand(OnCheckoutBranchAsync, () => _branchAtHead is not null);
        CreateBranchCommand = new AsyncRelayCommand(OnCreateBranchAsync, () => _context.Head is { IsDetached: true });

        _context.RepositoryChanged += (_, _) => ReadSuperprojectAsync().Forget();
        _context.StateRefreshed += (_, _) => UpdateBand();

        // Built after the repository opened — a window started on a path — it reads at once.
        if (_context.IsRepositoryOpen)
        {
            ReadSuperprojectAsync().Forget();
        }
    }

    /// <summary>
    /// Gets the folder of the repository this one is a submodule of, or <see langword="null"/> when it
    /// is not one.
    /// </summary>
    public string? SuperprojectPath { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets a value indicating whether the window is open on a submodule.</summary>
    public bool IsSubmodule => SuperprojectPath is not null;

    /// <summary>
    /// Gets what the mark after the repository's name says: "Submodule libs/Common of MainApp — its
    /// folder".
    /// </summary>
    public string MarkTip { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets a value indicating whether the band about being on no branch is on screen.</summary>
    public bool IsDetachedBandVisible { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets what the band says.</summary>
    public string DetachedMessage => "You're on no branch — commits made here are easy to lose.";

    /// <summary>
    /// Gets the band's checkout offer — "Check out main" — or an empty string when no branch is at
    /// HEAD.
    /// </summary>
    public string CheckoutBranchHeader { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets a value indicating whether a branch is at HEAD to offer.</summary>
    public bool OffersCheckout => _branchAtHead is not null;

    /// <summary>Gets the command that checks the branch at HEAD out.</summary>
    public AsyncRelayCommand CheckoutBranchCommand { get; }

    /// <summary>Gets the command that creates a branch at HEAD, in the usual dialog.</summary>
    public AsyncRelayCommand CreateBranchCommand { get; }

    /// <summary>
    /// Builds what the mark says.
    /// </summary>
    /// <param name="repositoryPath">The submodule's folder.</param>
    /// <param name="superprojectPath">The superproject's folder.</param>
    /// <returns>"Submodule libs/Common of MainApp — C:\src\MainApp".</returns>
    public static string DescribeMark(string repositoryPath, string superprojectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(superprojectPath);

        string path = Path.GetRelativePath(superprojectPath, repositoryPath).Replace(Path.DirectorySeparatorChar, '/');
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(superprojectPath));

        return $"Submodule {path} of {name} — {superprojectPath}";
    }

    /// <summary>
    /// Reads whether the open repository is a submodule, and whose.
    /// </summary>
    /// <returns>A task that completes once the mark and the band are up to date.</returns>
    public async Task ReadSuperprojectAsync()
    {
        RepositoryHandle? repository = _context.Repository;
        string? superproject = null;

        if (repository is not null)
        {
            try
            {
                superproject = await _submodules.ReadSuperprojectAsync(repository, _context.RepositoryLifetime).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (GitCommandException exception)
            {
                _logger.LogWarning(exception, "Whether {Repository} is a submodule could not be read", repository);
            }
        }

        if (!ReferenceEquals(repository, _context.Repository))
        {
            return;
        }

        SuperprojectPath = superproject;
        MarkTip = repository is not null && superproject is not null ? DescribeMark(repository.WorkTreePath, superproject) : string.Empty;
        OnPropertyChanged(nameof(IsSubmodule));
        UpdateBand();
    }

    private void UpdateBand()
    {
        HeadState? head = _context.Head;
        bool detached = IsSubmodule && head is { IsDetached: true, IsUnborn: false };

        _branchAtHead = detached ? FindBranchAt(head!.Sha) : null;
        CheckoutBranchHeader = _branchAtHead is { } branch ? $"Check out {branch.ShortName}" : string.Empty;
        IsDetachedBandVisible = detached;

        OnPropertyChanged(nameof(OffersCheckout));
        CheckoutBranchCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Finds the branch to offer at a commit: a local one first, else a remote one — never a remote's
    /// <c>HEAD</c>, which only names another of its branches.
    /// </summary>
    private GitBranch? FindBranchAt(string sha)
    {
        RefCollection refs = _context.Refs;

        return refs.LocalBranches.FirstOrDefault(branch => string.Equals(branch.TargetSha, sha, StringComparison.OrdinalIgnoreCase))
            ?? refs.RemoteBranches.FirstOrDefault(branch =>
                string.Equals(branch.TargetSha, sha, StringComparison.OrdinalIgnoreCase)
                && !branch.ShortName.EndsWith("/HEAD", StringComparison.Ordinal));
    }

    private Task OnCheckoutBranchAsync()
        => _branchAtHead is { } branch ? _branches.CheckoutAsync(branch.ShortName, branch.IsRemote) : Task.CompletedTask;

    private Task OnCreateBranchAsync()
        => _context.Head is { IsDetached: true } head
            ? _branches.CreateAsync(head.Sha, head.Sha.Length > 7 ? head.Sha[..7] : head.Sha)
            : Task.CompletedTask;
}
