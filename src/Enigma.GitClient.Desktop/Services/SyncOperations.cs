using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Enigma.GitClient.Desktop.Controls;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

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

    /// <summary>
    /// Pushes one tag, lightweight or annotated, to the remote the current branch pushes to — or
    /// <c>origin</c> when it has no upstream.
    /// </summary>
    /// <param name="tag">The tag's name.</param>
    /// <returns><see langword="true"/> when the remote has the tag.</returns>
    Task<bool> PushTagAsync(string tag);

    /// <summary>
    /// Fetches from every remote without showing anything: no overlay, no notification, whatever
    /// happens — the automatic refresh's fetch.
    /// </summary>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>What happened; a failure is logged, never reported.</returns>
    Task<QuietFetchResult> FetchQuietlyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What a quiet fetch came to.
/// </summary>
public enum QuietFetchResult
{
    /// <summary>The fetch ran, and the reference state was re-read after it.</summary>
    Fetched,

    /// <summary>Another operation held the repository, so nothing ran.</summary>
    Skipped,

    /// <summary>The fetch ran and failed — offline, no credentials, a remote gone.</summary>
    Failed,
}

/// <summary>
/// Default <see cref="ISyncOperations"/>.
/// </summary>
public sealed class SyncOperations : ISyncOperations
{
    private readonly IRepositoryContext _context;
    private readonly ISyncService _sync;
    private readonly IPushGuard _pushGuard;
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
    /// <param name="pushGuard">Says whether the repository's profile may push to the remote.</param>
    /// <param name="settings">Supplies the pull strategy.</param>
    /// <param name="overlay">Shows the progress.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public SyncOperations(
        IRepositoryContext context,
        ISyncService sync,
        IPushGuard pushGuard,
        ISettingsService settings,
        IOverlayService overlay,
        IInfoBarService infoBar,
        ILogger<SyncOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(pushGuard);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _sync = sync;
        _pushGuard = pushGuard;
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
    public async Task<bool> PushAsync(bool setUpstream = false)
    {
        PushRequest request = new()
        {
            Remote = _context.Refs.CurrentBranch?.RemoteName ?? GitRemote.DefaultName,
            Branch = _context.Head?.BranchName,
            SetUpstream = setUpstream || _context.Refs.CurrentBranch?.UpstreamShortName is null,
            PushTags = true,
        };

        if (!await MayPushAsync(request.Remote).ConfigureAwait(true))
        {
            return false;
        }

        return await RunAsync(
                "Pushing",
                (handle, progress, token) => _sync.PushAsync(handle, request, progress, token),
                "Pushed",
                "The remote has your commits.")
            .ConfigureAwait(true);
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
            Report(
                $"Nothing to pull into \"{branch}\"",
                $"\"{branch}\" has no upstream to pull from. Give it one with \"Set upstream\" in the branches, or push it once.",
                InfoBarSeverity.Info);
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

        if (!await MayPushAsync(request.Remote).ConfigureAwait(true))
        {
            return false;
        }

        return await RunAsync(
                $"Pushing {branch}",
                (handle, progress, token) => _sync.PushAsync(handle, request, progress, token),
                "Pushed",
                $"The remote has the commits of \"{branch}\".")
            .ConfigureAwait(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The remote is chosen as a push of the current branch chooses it, so the two never disagree.
    /// A remote that already has the tag on another commit refuses it; git's sentence for that talks
    /// about pulling commits, which is not what happened, so it gets one of its own.
    /// </remarks>
    public async Task<bool> PushTagAsync(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        string? upstream = _context.Refs.CurrentBranch is { Tracking.IsUpstreamGone: false } current
            ? current.UpstreamShortName
            : null;

        string remote = upstream is { Length: > 0 } ? SplitUpstream(upstream).Remote : GitRemote.DefaultName;

        if (!await MayPushAsync(remote).ConfigureAwait(true))
        {
            return false;
        }

        return await RunAsync(
                $"Pushing {tag}",
                (handle, progress, token) => _sync.PushTagAsync(handle, remote, tag, progress, token),
                "Pushed",
                $"{remote} has the tag \"{tag}\".",
                failure => failure.Kind == SyncFailureKind.NonFastForward
                    ? $"{remote} already has a tag \"{tag}\", on another commit. Nothing was replaced: "
                        + "delete it there first, or give this tag another name."
                    : null)
            .ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<QuietFetchResult> FetchQuietlyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            bool ran = await _context
                .TryRunExclusiveAsync(
                    (handle, token) => _sync.FetchAsync(handle, null, true, true, null, token),
                    true,
                    cancellationToken)
                .ConfigureAwait(true);

            return ran ? QuietFetchResult.Fetched : QuietFetchResult.Skipped;
        }
        catch (OperationCanceledException)
        {
            return QuietFetchResult.Skipped;
        }
        catch (SyncException exception)
        {
            // Debug, not warning: an offline laptop fails this every few seconds.
            _logger.LogDebug(exception, "The automatic fetch failed: {Kind}", exception.Failure.Kind);
            return QuietFetchResult.Failed;
        }
        catch (GitCommandException exception)
        {
            _logger.LogDebug(exception, "The automatic fetch failed");
            return QuietFetchResult.Failed;
        }
    }

    /// <summary>
    /// Asks whether the repository's profile may push to a remote, and says why not when it may not.
    /// </summary>
    /// <param name="remote">The remote's name.</param>
    /// <returns><see langword="true"/> when the push may go ahead.</returns>
    private async Task<bool> MayPushAsync(string? remote)
    {
        // Nothing open, or no remote named: the push itself reports that, as it always has.
        if (_context.Repository is not { } repository || string.IsNullOrWhiteSpace(remote))
        {
            return true;
        }

        PushPermission permission;

        try
        {
            permission = await _pushGuard.CheckAsync(repository, remote, _context.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (permission.IsAllowed)
        {
            return true;
        }

        (string title, string message) = DescribeRefusal(permission);
        Report(title, message, InfoBarSeverity.Warning);

        return false;
    }

    /// <summary>
    /// Says why a push was refused, and what to do about it.
    /// </summary>
    /// <param name="permission">The refusal.</param>
    /// <returns>The notification's title and message.</returns>
    internal static (string Title, string Message) DescribeRefusal(PushPermission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (permission.Reason == PushPermissionReason.CheckFailed || permission.Profile is not { } profile)
        {
            return (
                "Nothing was pushed",
                "The profile this repository commits as could not be checked. Try again, or look at the Profiles page.");
        }

        return (
            $"{profile.Label} does not push to {permission.Target}",
            $"Nothing was pushed: the profile {profile.Label} has no integration for {permission.Target}. "
            + $"To push there, connect an account for it to {profile.Label} on the Profiles page.");
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

            Report(successTitle, successMessage, InfoBarSeverity.Success);
            return true;
        }
        catch (OperationCanceledException)
        {
            Report($"{title} cancelled", "The transfer was stopped.", InfoBarSeverity.Info);
        }
        catch (SyncException exception)
        {
            _logger.LogWarning(exception, "{Title} failed: {Kind}", title, exception.Failure.Kind);

            Report(
                $"{title} failed",
                explain?.Invoke(exception.Failure) ?? exception.Failure.Message,

                // A failure the user can fix themselves is a warning; one that needs their
                // credentials or their host configuration is an error.
                exception.Failure.IsRecoverableLocally ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title} failed", title);

            Report($"{title} failed", SyncErrorMapper.FirstMeaningfulLine(exception.StandardError),
                InfoBarSeverity.Error);
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

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
