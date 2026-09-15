using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// One branch, as the branches page shows it.
/// </summary>
/// <remarks>
/// The row carries the page's commands rather than raising events, so a context menu opening in its
/// own popup tree can still reach them with a plain binding against the row itself.
/// </remarks>
public sealed class BranchRowViewModel : ViewModelBase
{
    private readonly BranchesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="branch">The branch it stands for.</param>
    public BranchRowViewModel(BranchesPageViewModel owner, GitBranch branch)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(branch);

        _owner = owner;
        Branch = branch;
    }

    /// <summary>Gets the branch this row stands for.</summary>
    public GitBranch Branch { get; }

    /// <summary>Gets the name shown on the row.</summary>
    public string Name => Branch.IsRemote ? Branch.NameWithoutRemote : Branch.ShortName;

    /// <summary>Gets the branch's full short name, which commands are given.</summary>
    public string FullName => Branch.ShortName;

    /// <summary>Gets a value indicating whether this is the checked-out branch.</summary>
    public bool IsCurrent => Branch.IsCurrent;

    /// <summary>Gets a value indicating whether the branch lives on a remote.</summary>
    public bool IsRemote => Branch.IsRemote;

    /// <summary>Gets the upstream the branch tracks, empty when it tracks none.</summary>
    public string Upstream => Branch.UpstreamShortName ?? string.Empty;

    /// <summary>Gets a value indicating whether there is an upstream to show.</summary>
    public bool HasUpstream => Upstream.Length > 0;

    /// <summary>Gets a value indicating whether the upstream the branch names has gone.</summary>
    public bool IsUpstreamGone => Branch.Tracking.IsUpstreamGone;

    /// <summary>Gets how many commits the branch is ahead of its upstream.</summary>
    public string Ahead => Branch.Tracking.Ahead.ToString(CultureInfo.CurrentCulture);

    /// <summary>Gets how many commits the branch is behind its upstream.</summary>
    public string Behind => Branch.Tracking.Behind.ToString(CultureInfo.CurrentCulture);

    /// <summary>Gets a value indicating whether the branch has commits its upstream does not.</summary>
    public bool IsAhead => Branch.Tracking.Ahead > 0;

    /// <summary>Gets a value indicating whether the upstream has commits the branch does not.</summary>
    public bool IsBehind => Branch.Tracking.Behind > 0;

    /// <summary>Gets the subject of the branch's tip commit.</summary>
    public string TipSubject => Branch.TipSubject;

    /// <summary>Gets who wrote the tip commit.</summary>
    public string TipAuthor => Branch.TipAuthor.Name;

    /// <summary>Gets how long ago the tip commit was written.</summary>
    public string TipDate => RelativeTime.Format(Branch.TipDate);

    /// <summary>Gets the tip commit's short hash.</summary>
    public string ShortSha => Branch.TargetSha.Length >= 7 ? Branch.TargetSha[..7] : Branch.TargetSha;

    /// <summary>Gets the command that checks the branch out.</summary>
    public AsyncRelayCommand<BranchRowViewModel> CheckoutCommand => _owner.CheckoutCommand;

    /// <summary>Gets the command that renames the branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> RenameCommand => _owner.RenameCommand;

    /// <summary>Gets the command that deletes the branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> DeleteCommand => _owner.DeleteCommand;

    /// <summary>Gets the command that points the branch at an upstream.</summary>
    public AsyncRelayCommand<BranchRowViewModel> SetUpstreamCommand => _owner.SetUpstreamCommand;

    /// <inheritdoc />
    public override string ToString() => FullName;
}

/// <summary>
/// One tag, as the page shows it.
/// </summary>
public sealed class TagRowViewModel : ViewModelBase
{
    private readonly BranchesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="tag">The tag it stands for.</param>
    public TagRowViewModel(BranchesPageViewModel owner, GitTag tag)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(tag);

        _owner = owner;
        Tag = tag;
    }

    /// <summary>Gets the tag this row stands for.</summary>
    public GitTag Tag { get; }

    /// <summary>Gets the tag's name.</summary>
    public string Name => Tag.ShortName;

    /// <summary>Gets whether the tag is annotated or a plain pointer.</summary>
    public string Kind => Tag.IsAnnotated ? "annotated" : "lightweight";

    /// <summary>Gets the tag's message, empty for a lightweight tag.</summary>
    public string Message => Tag.Message;

    /// <summary>Gets a value indicating whether there is a message to show.</summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>Gets who created the tag, empty for a lightweight tag.</summary>
    public string Tagger => Tag.Tagger?.Name ?? string.Empty;

    /// <summary>Gets a value indicating whether there is a tagger to show.</summary>
    public bool HasTagger => Tagger.Length > 0;

    /// <summary>Gets how long ago the tag's commit was written.</summary>
    public string Date => RelativeTime.Format(Tag.TargetDate);

    /// <summary>Gets the tagged commit's short hash.</summary>
    public string ShortSha => Tag.TargetSha.Length >= 7 ? Tag.TargetSha[..7] : Tag.TargetSha;

    /// <summary>Gets the command that checks the tag out, detaching HEAD.</summary>
    public AsyncRelayCommand<TagRowViewModel> CheckoutCommand => _owner.CheckoutTagCommand;

    /// <summary>Gets the command that deletes the tag.</summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteCommand => _owner.DeleteTagCommand;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// A group of branches on the page: the local ones, or one remote's.
/// </summary>
/// <param name="Title">The heading shown above the group.</param>
/// <param name="IsRemote">Whether the group holds remote branches.</param>
/// <param name="Rows">The branches in the group.</param>
public sealed record BranchGroupViewModel(string Title, bool IsRemote, IReadOnlyList<BranchRowViewModel> Rows)
{
    /// <summary>Gets how many branches the group holds, for its heading.</summary>
    public string Count => Rows.Count.ToString(CultureInfo.CurrentCulture);
}

/// <summary>
/// ViewModel behind the branches page.
/// </summary>
/// <remarks>
/// The page shows and filters; every write, with its dialog and its confirmation, belongs to
/// <see cref="IBranchOperations"/>, which the graph's own context menu calls too. Two places
/// offering the same operation have to ask the same questions.
/// </remarks>
public sealed class BranchesPageViewModel : PageViewModelBase
{
    private readonly IBranchOperations _operations;
    private readonly ITagOperations _tagOperations;
    private readonly ICheckoutOperations _checkoutOperations;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="operations">Performs the branch operations, dialogs and all.</param>
    /// <param name="tagOperations">Performs the tag operations.</param>
    /// <param name="checkoutOperations">Performs a checkout, including the questions it has to ask.</param>
    public BranchesPageViewModel(
        IRepositoryContext repositoryContext,
        IBranchOperations operations,
        ITagOperations tagOperations,
        ICheckoutOperations checkoutOperations)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(tagOperations);
        ArgumentNullException.ThrowIfNull(checkoutOperations);

        _operations = operations;
        _tagOperations = tagOperations;
        _checkoutOperations = checkoutOperations;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRepositoryOpen);
        CreateBranchCommand = new AsyncRelayCommand(OnCreateAsync, () => IsRepositoryOpen);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

        CheckoutCommand = new AsyncRelayCommand<BranchRowViewModel>(OnCheckoutAsync, CanCheckout);
        RenameCommand = new AsyncRelayCommand<BranchRowViewModel>(OnRenameAsync, IsLocal);
        DeleteCommand = new AsyncRelayCommand<BranchRowViewModel>(OnDeleteAsync, CanDelete);
        SetUpstreamCommand = new AsyncRelayCommand<BranchRowViewModel>(OnSetUpstreamAsync, IsLocal);

        ShowBranchesCommand = new RelayCommand(() => ShowTags = false);
        ShowTagsCommand = new RelayCommand(() => ShowTags = true);
        CreateTagCommand = new AsyncRelayCommand(OnCreateTagAsync, () => IsRepositoryOpen);
        CheckoutTagCommand = new AsyncRelayCommand<TagRowViewModel>(OnCheckoutTagAsync, row => row is not null);
        DeleteTagCommand = new AsyncRelayCommand<TagRowViewModel>(OnDeleteTagAsync, row => row is not null);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Branches and tags";

    /// <summary>Gets the tags the repository holds.</summary>
    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the tag list is shown instead of the branches.
    /// </summary>
    public bool ShowTags
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ShowBranches));
                OnPropertyChanged(nameof(SearchPlaceholder));
                Rebuild();
            }
        }
    }

    /// <summary>Gets a value indicating whether the branch list is shown.</summary>
    public bool ShowBranches => !ShowTags;

    /// <summary>
    /// Gets what the filter box says it filters, which follows whichever list is shown.
    /// </summary>
    public string SearchPlaceholder => ShowTags ? "Filter tags" : "Filter branches";

    /// <summary>Gets the branches, grouped as local and one group per remote.</summary>
    public ObservableCollection<BranchGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// Gets or sets a substring the shown branch names must contain.
    /// </summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                Rebuild();
            }
        }
    } = string.Empty;

    /// <summary>Gets a value indicating whether the page has nothing to show.</summary>
    public bool IsEmpty => ShowTags ? Tags.Count == 0 : Groups.Count == 0;

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to manage its branches and tags."
            : SearchText.Trim().Length > 0
                ? ShowTags ? "No tag matches this search." : "No branch matches this search."
                : ShowTags
                    ? "This repository has no tags yet."
                    : "This repository has no branches yet. The first commit creates one.";

    /// <summary>Gets the command that re-reads the references.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that opens the create-branch dialog.</summary>
    public AsyncRelayCommand CreateBranchCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that checks a branch out.</summary>
    public AsyncRelayCommand<BranchRowViewModel> CheckoutCommand { get; }

    /// <summary>Gets the command that renames a branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> RenameCommand { get; }

    /// <summary>Gets the command that deletes a branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> DeleteCommand { get; }

    /// <summary>Gets the command that points a branch at an upstream.</summary>
    public AsyncRelayCommand<BranchRowViewModel> SetUpstreamCommand { get; }

    /// <summary>Gets the command that shows the branch list.</summary>
    public RelayCommand ShowBranchesCommand { get; }

    /// <summary>Gets the command that shows the tag list.</summary>
    public RelayCommand ShowTagsCommand { get; }

    /// <summary>Gets the command that opens the create-tag dialog.</summary>
    public AsyncRelayCommand CreateTagCommand { get; }

    /// <summary>Gets the command that checks a tag out, detaching HEAD.</summary>
    public AsyncRelayCommand<TagRowViewModel> CheckoutTagCommand { get; }

    /// <summary>Gets the command that deletes a tag.</summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteTagCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        Rebuild();
    }

    /// <summary>
    /// Re-reads the repository's references and rebuilds the page.
    /// </summary>
    /// <returns>A task that completes once the page is up to date.</returns>
    public async Task RefreshAsync()
    {
        if (IsRepositoryOpen)
        {
            await RepositoryContext.RefreshAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }

        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        RefreshCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        CreateTagCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => Rebuild();

    /// <summary>
    /// Rebuilds the groups from whatever the repository context last read.
    /// </summary>
    private void Rebuild()
    {
        Groups.Clear();

        RefCollection refs = RepositoryContext.Refs;

        List<BranchRowViewModel> local = [];

        foreach (GitBranch branch in refs.LocalBranches)
        {
            if (Matches(branch))
            {
                local.Add(new BranchRowViewModel(this, branch));
            }
        }

        if (local.Count > 0)
        {
            Groups.Add(new BranchGroupViewModel("Local", false, local));
        }

        // One group per remote, in the order the remotes' branches were read, so a repository with
        // several remotes does not shuffle between refreshes.
        Dictionary<string, List<BranchRowViewModel>> byRemote = new(StringComparer.Ordinal);
        List<string> order = [];

        foreach (GitBranch branch in refs.RemoteBranches)
        {
            if (!Matches(branch))
            {
                continue;
            }

            string remote = branch.RemoteName ?? "remote";

            if (!byRemote.TryGetValue(remote, out List<BranchRowViewModel>? rows))
            {
                rows = [];
                byRemote[remote] = rows;
                order.Add(remote);
            }

            rows.Add(new BranchRowViewModel(this, branch));
        }

        foreach (string remote in order)
        {
            Groups.Add(new BranchGroupViewModel(remote, true, byRemote[remote]));
        }

        Tags.Clear();

        foreach (GitTag tag in refs.Tags)
        {
            if (Matches(tag.ShortName))
            {
                Tags.Add(new TagRowViewModel(this, tag));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private bool Matches(GitBranch branch) => Matches(branch.ShortName);

    private bool Matches(string name)
    {
        string search = SearchText.Trim();

        return search.Length == 0 || name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocal(BranchRowViewModel? row) => row is { IsRemote: false };

    private static bool CanCheckout(BranchRowViewModel? row) => row is not null && !row.IsCurrent;

    private static bool CanDelete(BranchRowViewModel? row) => row is { IsCurrent: false };

    // ---------------------------------------------------------------- commands

    private async Task OnCheckoutAsync(BranchRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // A remote branch is checked out by creating the tracking branch for it, which is the
        // branch service's job; a local one goes through the checkout flow, so the questions about
        // uncommitted work get asked.
        await Run(() => row.IsRemote
            ? _operations.CheckoutAsync(row.FullName, true)
            : _checkoutOperations.CheckoutAsync(row.FullName)).ConfigureAwait(true);
    }

    private Task OnCreateTagAsync() => Run(() => _tagOperations.CreateAsync());

    private async Task OnCheckoutTagAsync(TagRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _checkoutOperations.CheckoutAsync(row.Name)).ConfigureAwait(true);
        }
    }

    private async Task OnDeleteTagAsync(TagRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _tagOperations.DeleteAsync(row.Name)).ConfigureAwait(true);
        }
    }

    private Task OnCreateAsync() => Run(() => _operations.CreateAsync());

    private async Task OnRenameAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _operations.RenameAsync(row.FullName)).ConfigureAwait(true);
        }
    }

    private async Task OnDeleteAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _operations.DeleteAsync(row.FullName, row.IsRemote)).ConfigureAwait(true);
        }
    }

    private async Task OnSetUpstreamAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _operations.SetUpstreamAsync(row.FullName, row.HasUpstream ? row.Upstream : null))
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Runs one operation with the page marked busy, and rebuilds when it changed something.
    /// </summary>
    private async Task Run(Func<Task<bool>> operation)
    {
        IsBusy = true;

        try
        {
            if (await operation().ConfigureAwait(true))
            {
                Rebuild();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
