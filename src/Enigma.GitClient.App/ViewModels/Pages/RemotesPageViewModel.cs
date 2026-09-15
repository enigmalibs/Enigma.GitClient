using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// One remote, as the page shows it.
/// </summary>
public sealed class RemoteRowViewModel : ViewModelBase
{
    private readonly RemotesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="remote">The remote it stands for.</param>
    /// <param name="branchCount">How many tracking branches the remote has here.</param>
    public RemoteRowViewModel(RemotesPageViewModel owner, GitRemote remote, int branchCount)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(remote);

        _owner = owner;
        Remote = remote;
        BranchCount = branchCount;
    }

    /// <summary>Gets the remote this row stands for.</summary>
    public GitRemote Remote { get; }

    /// <summary>Gets the remote's name.</summary>
    public string Name => Remote.Name;

    /// <summary>Gets the URL fetches read from.</summary>
    public string FetchUrl => Remote.FetchUrl;

    /// <summary>Gets the URL pushes write to.</summary>
    public string PushUrl => Remote.PushUrl;

    /// <summary>Gets a value indicating whether pushes go somewhere else than fetches come from.</summary>
    public bool HasSeparatePushUrl => Remote.HasSeparatePushUrl;

    /// <summary>Gets the host the remote lives on, empty when the URL is a local path.</summary>
    public string Host => Remote.Host ?? string.Empty;

    /// <summary>Gets a value indicating whether there is a host worth naming.</summary>
    public bool HasHost => Host.Length > 0;

    /// <summary>Gets how many tracking branches this remote has here.</summary>
    public int BranchCount { get; }

    /// <summary>Gets that count as the row shows it.</summary>
    public string BranchSummary
        => BranchCount == 1
            ? "1 branch"
            : $"{BranchCount.ToString(System.Globalization.CultureInfo.CurrentCulture)} branches";

    /// <summary>Gets the command that edits the remote.</summary>
    public AsyncRelayCommand<RemoteRowViewModel> EditCommand => _owner.EditCommand;

    /// <summary>Gets the command that removes the remote.</summary>
    public AsyncRelayCommand<RemoteRowViewModel> RemoveCommand => _owner.RemoveCommand;

    /// <summary>Gets the command that fetches from this remote alone.</summary>
    public AsyncRelayCommand<RemoteRowViewModel> FetchCommand => _owner.FetchCommand;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// ViewModel behind the remotes page.
/// </summary>
public sealed class RemotesPageViewModel : PageViewModelBase
{
    private readonly IRemoteService _remotes;
    private readonly ISyncService _sync;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<RemotesPageViewModel> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="remotes">Manages the remotes.</param>
    /// <param name="sync">Fetches from one of them.</param>
    /// <param name="dialogs">Raises the forms and the confirmations.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public RemotesPageViewModel(
        IRepositoryContext repositoryContext,
        IRemoteService remotes,
        ISyncService sync,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<RemotesPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _remotes = remotes;
        _sync = sync;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRepositoryOpen);
        AddCommand = new AsyncRelayCommand(OnAddAsync, () => IsRepositoryOpen);
        EditCommand = new AsyncRelayCommand<RemoteRowViewModel>(OnEditAsync, row => row is not null);
        RemoveCommand = new AsyncRelayCommand<RemoteRowViewModel>(OnRemoveAsync, row => row is not null);
        FetchCommand = new AsyncRelayCommand<RemoteRowViewModel>(OnFetchAsync, row => row is not null);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Remotes";

    /// <summary>Gets the remotes the repository knows about.</summary>
    public ObservableCollection<RemoteRowViewModel> Remotes { get; } = [];

    /// <summary>Gets a value indicating whether the page has nothing to show.</summary>
    public bool IsEmpty => Remotes.Count == 0;

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage
        => IsRepositoryOpen
            ? "This repository has no remotes. Add one to fetch from and push to."
            : "Open a repository to manage its remotes.";

    /// <summary>Gets the command that re-reads the remotes.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that adds a remote.</summary>
    public AsyncRelayCommand AddCommand { get; }

    /// <summary>Gets the command that edits a remote.</summary>
    public AsyncRelayCommand<RemoteRowViewModel> EditCommand { get; }

    /// <summary>Gets the command that removes a remote.</summary>
    public AsyncRelayCommand<RemoteRowViewModel> RemoveCommand { get; }

    /// <summary>Gets the command that fetches from one remote.</summary>
    public AsyncRelayCommand<RemoteRowViewModel> FetchCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the remotes.
    /// </summary>
    /// <returns>A task that completes once the page is up to date.</returns>
    public async Task RefreshAsync()
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        IReadOnlyList<GitRemote> remotes = [];

        if (repository is not null)
        {
            try
            {
                remotes = await _remotes
                    .ListAsync(repository, RepositoryContext.RepositoryLifetime)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Expected when the repository changes under the read.
            }
            catch (GitCommandException exception)
            {
                _logger.LogError(exception, "Reading the remotes failed");
            }
        }

        // Read first, replace after. Clearing before the await lets a second refresh — and one
        // arrives on every state change — interleave with this one and list every remote twice.
        Remotes.Clear();

        foreach (GitRemote remote in remotes)
        {
            Remotes.Add(new RemoteRowViewModel(this, remote, CountBranches(remote.Name)));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        RefreshCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();

        _ = RefreshAsync();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => _ = RefreshAsync();

    private int CountBranches(string remote)
    {
        int count = 0;

        foreach (GitBranch branch in RepositoryContext.Refs.RemoteBranches)
        {
            if (string.Equals(branch.RemoteName, remote, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // ---------------------------------------------------------------- commands

    private async Task OnAddAsync()
    {
        RemoteDialogViewModel model = new(TakenNames(), null);

        await ShowFormAsync(
            "Add a remote",
            model,
            "Add",
            (handle, token) => _remotes.AddAsync(handle, model.Name, model.FetchUrl, token),
            () => $"Could not add \"{model.Name}\"").ConfigureAwait(true);
    }

    private async Task OnEditAsync(RemoteRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        RemoteDialogViewModel model = new(TakenNames(), row.Remote);

        await ShowFormAsync(
            $"Edit {row.Name}",
            model,
            "Save",
            async (handle, token) =>
            {
                if (!string.Equals(model.Name, row.Name, StringComparison.Ordinal))
                {
                    await _remotes.RenameAsync(handle, row.Name, model.Name, token).ConfigureAwait(true);
                }

                await _remotes
                    .SetUrlAsync(handle, model.Name, model.FetchUrl, model.EffectivePushUrl, token)
                    .ConfigureAwait(true);
            },
            () => $"Could not update \"{row.Name}\"").ConfigureAwait(true);
    }

    private async Task OnRemoveAsync(RemoteRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Remove remote";
            dialog.Content =
                $"Remove \"{row.Name}\"? Its {row.BranchSummary} of tracking references go with it.\n\n"
                + "Nothing on the remote itself is touched.";
            dialog.PrimaryButtonText = "Remove";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return;
        }

        await RunAsync(
            (handle, token) => _remotes.RemoveAsync(handle, row.Name, token),
            $"Could not remove \"{row.Name}\"").ConfigureAwait(true);
    }

    private async Task OnFetchAsync(RemoteRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        bool done = await RunAsync(
            (handle, token) => _sync.FetchAsync(handle, row.Name, true, true, null, token),
            $"Could not fetch from \"{row.Name}\"").ConfigureAwait(true);

        if (done)
        {
            await ReportAsync("Fetched", $"Everything \"{row.Name}\" had is here.", InfoBarSeverity.Success)
                .ConfigureAwait(true);
        }
    }

    // ---------------------------------------------------------------- plumbing

    private IReadOnlyCollection<string> TakenNames()
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (RemoteRowViewModel row in Remotes)
        {
            names.Add(row.Name);
        }

        return names;
    }

    private async Task ShowFormAsync(
        string title,
        RemoteDialogViewModel model,
        string primaryButtonText,
        Func<RepositoryHandle, CancellationToken, Task> operation,
        Func<string> failureTitle)
    {
        RemoteDialogView view = new() { DataContext = model };

        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = model.IsValid;
            }
        }

        model.ValidationChanged += OnValidationChanged;

        DialogResult result;

        try
        {
            result = await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = title;
                dialog.Content = view;
                dialog.PrimaryButtonText = primaryButtonText;
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = model.IsValid;
            }).ConfigureAwait(true);
        }
        finally
        {
            model.ValidationChanged -= OnValidationChanged;
        }

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return;
        }

        await RunAsync(operation, failureTitle()).ConfigureAwait(true);
    }

    private async Task<bool> RunAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        string failureTitle)
    {
        if (!IsRepositoryOpen)
        {
            return false;
        }

        IsBusy = true;

        try
        {
            await RepositoryContext.RunExclusiveAsync(operation).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);

            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            await ReportAsync(failureTitle, refusal.Message, InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (SyncException exception)
        {
            await ReportAsync(failureTitle, exception.Failure.Message, InfoBarSeverity.Error).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            await ReportAsync(failureTitle, SyncErrorMapper.FirstMeaningfulLine(exception.StandardError),
                InfoBarSeverity.Error).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
