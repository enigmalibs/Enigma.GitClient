using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Fetch, pull and push as a user performs them: the progress overlay with a cancel that works, and
/// a sentence afterwards that says what to do next when something went wrong.
/// </summary>
public interface ISyncOperations
{
    /// <summary>
    /// Fetches from every remote.
    /// </summary>
    /// <returns><see langword="true"/> when the fetch finished.</returns>
    Task<bool> FetchAsync();

    /// <summary>
    /// Pulls into the current branch.
    /// </summary>
    /// <returns><see langword="true"/> when the pull finished.</returns>
    Task<bool> PullAsync();

    /// <summary>
    /// Pushes the current branch, setting its upstream when it has none.
    /// </summary>
    /// <param name="setUpstream">Whether to record the upstream this pushes to.</param>
    /// <returns><see langword="true"/> when the push finished.</returns>
    Task<bool> PushAsync(bool setUpstream = false);

    /// <summary>
    /// Brings a local branch up to date with its upstream: the current branch through a pull, any
    /// other one by fast-forwarding it — which never moves HEAD and never merges.
    /// </summary>
    /// <param name="branch">The local branch's name.</param>
    /// <returns><see langword="true"/> when the branch was brought up to date.</returns>
    Task<bool> PullBranchAsync(string branch);

    /// <summary>
    /// Pushes a local branch, current or not, setting its upstream when it has none.
    /// </summary>
    /// <param name="branch">The local branch's name.</param>
    /// <returns><see langword="true"/> when the push finished.</returns>
    Task<bool> PushBranchAsync(string branch);
}

/// <summary>
/// Default <see cref="ISyncOperations"/>.
/// </summary>
public sealed class SyncOperations : ISyncOperations
{
    private readonly IRepositoryContext _context;
    private readonly ISyncService _sync;
    private readonly ISettingsService _settings;
    private readonly IOverlayService _overlay;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<SyncOperations> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="sync">Performs the transfer.</param>
    /// <param name="overlay">Shows the progress.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public SyncOperations(
        IRepositoryContext context,
        ISyncService sync,
        ISettingsService settings,
        IOverlayService overlay,
        IInfoBarService infoBar,
        ILogger<SyncOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _sync = sync;
        _settings = settings;
        _overlay = overlay;
        _infoBar = infoBar;
        _logger = logger;

        CancelCommand = new RelayCommand(() => _cancellation?.Cancel());
    }

    /// <summary>
    /// Gets the command the progress card's Cancel button runs.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <inheritdoc />
    public Task<bool> FetchAsync()
        => RunAsync(
            "Fetching",
            (handle, progress, token) => _sync.FetchAsync(handle, null, true, true, progress, token),
            "Fetched",
            "Everything the remotes had is here.");

    /// <inheritdoc />
    public Task<bool> PullAsync()
        => RunAsync(
            "Pulling",
            (handle, progress, token) => _sync.PullAsync(handle, null, null, _settings.Current.Pull, progress, token),
            "Pulled",
            "The branch is up to date with its upstream.");

    /// <inheritdoc />
    public Task<bool> PushAsync(bool setUpstream = false)
    {
        PushRequest request = new()
        {
            Remote = _context.Refs.CurrentBranch?.RemoteName ?? GitRemote.DefaultName,
            Branch = _context.Head?.BranchName,
            SetUpstream = setUpstream || _context.Refs.CurrentBranch?.UpstreamShortName is null,
            PushTags = true,
        };

        return RunAsync(
            "Pushing",
            (handle, progress, token) => _sync.PushAsync(handle, request, progress, token),
            "Pushed",
            "The remote has your commits.");
    }

    /// <inheritdoc />
    public async Task<bool> PullBranchAsync(string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        GitBranch? local = FindLocal(branch);

        if (local is null)
        {
            return false;
        }

        if (local.IsCurrent)
        {
            return await PullAsync().ConfigureAwait(true);
        }

        if (local.UpstreamShortName is not { Length: > 0 } upstream || local.Tracking.IsUpstreamGone)
        {
            await ReportAsync(
                    $"Nothing to pull into \"{branch}\"",
                    $"\"{branch}\" has no upstream to pull from. Give it one with \"Set upstream\" in the branches, or push it once.",
                    InfoBarSeverity.Info)
                .ConfigureAwait(true);
            return false;
        }

        (string remote, string remoteBranch) = SplitUpstream(upstream);

        return await RunAsync(
                $"Pulling {branch}",
                (handle, progress, token) => _sync.FastForwardBranchAsync(handle, remote, remoteBranch, branch, progress, token),
                "Pulled",
                $"\"{branch}\" is up to date with \"{upstream}\".",
                failure => failure.Kind == SyncFailureKind.NonFastForward
                    ? $"\"{branch}\" and \"{upstream}\" have both moved on, so it cannot simply be moved forward. "
                        + "Check it out and pull to merge them."
                    : null)
            .ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> PushBranchAsync(string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        GitBranch? local = FindLocal(branch);

        if (local is null)
        {
            return false;
        }

        string? upstream = local.Tracking.IsUpstreamGone ? null : local.UpstreamShortName;

        PushRequest request = new()
        {
            Remote = upstream is { Length: > 0 } ? SplitUpstream(upstream).Remote : GitRemote.DefaultName,
            Branch = branch,
            SetUpstream = upstream is null,
            PushTags = true,
        };

        return await RunAsync(
                $"Pushing {branch}",
                (handle, progress, token) => _sync.PushAsync(handle, request, progress, token),
                "Pushed",
                $"The remote has the commits of \"{branch}\".")
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The local branch of that name, as the context last read it.
    /// </summary>
    private GitBranch? FindLocal(string branch)
    {
        foreach (GitBranch candidate in _context.Refs.LocalBranches)
        {
            if (string.Equals(candidate.ShortName, branch, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits an upstream's short name — <c>origin/feature/login</c> — into its remote and the branch's
    /// name on that remote. The remote is the part before the first slash, which is how git abbreviates
    /// it.
    /// </summary>
    internal static (string Remote, string Branch) SplitUpstream(string upstream)
    {
        int separator = upstream.IndexOf('/');

        return separator <= 0
            ? (GitRemote.DefaultName, upstream)
            : (upstream[..separator], upstream[(separator + 1)..]);
    }

    private async Task<bool> RunAsync(
        string title,
        Func<RepositoryHandle, IProgress<SyncProgress>, CancellationToken, Task> operation,
        string successTitle,
        string successMessage,
        Func<SyncFailure, string?>? explain = null)
    {
        RepositoryHandle? repository = _context.Repository;

        if (repository is null)
        {
            return false;
        }

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_context.RepositoryLifetime);

        _cancellation = cancellation;

        ProgressOverlayCard card = new()
        {
            Title = title,
            Message = "Contacting the remote…",
            IsIndeterminate = true,
            CancelCommand = CancelCommand,
        };

        Progress<SyncProgress> progress = new(report => Apply(card, report));

        await _overlay.ShowAsync(card).ConfigureAwait(true);

        try
        {
            await _context
                .RunExclusiveAsync((handle, token) => operation(handle, progress, token), true, cancellation.Token)
                .ConfigureAwait(true);

            await ReportAsync(successTitle, successMessage, InfoBarSeverity.Success).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            await ReportAsync($"{title} cancelled", "The transfer was stopped.", InfoBarSeverity.Info)
                .ConfigureAwait(true);
        }
        catch (SyncException exception)
        {
            _logger.LogWarning(exception, "{Title} failed: {Kind}", title, exception.Failure.Kind);

            await ReportAsync(
                $"{title} failed",
                explain?.Invoke(exception.Failure) ?? exception.Failure.Message,

                // A failure the user can fix themselves is a warning; one that needs their
                // credentials or their host configuration is an error.
                exception.Failure.IsRecoverableLocally ? InfoBarSeverity.Warning : InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title} failed", title);

            await ReportAsync($"{title} failed", SyncErrorMapper.FirstMeaningfulLine(exception.StandardError),
                InfoBarSeverity.Error).ConfigureAwait(true);
        }
        finally
        {
            _cancellation = null;

            // Always: an overlay left open makes the whole window unusable.
            await _overlay.HideAsync().ConfigureAwait(true);
        }

        return false;
    }

    /// <summary>
    /// Moves the progress card to what git last said.
    /// </summary>
    /// <param name="card">The card on screen.</param>
    /// <param name="report">What git said.</param>
    public static void Apply(ProgressOverlayCard card, SyncProgress report)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(report);

        card.Message = report.Description;
        card.IsIndeterminate = !report.IsDeterminate;

        if (report.IsDeterminate)
        {
            card.Progress = report.Percent!.Value;
        }
    }

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
