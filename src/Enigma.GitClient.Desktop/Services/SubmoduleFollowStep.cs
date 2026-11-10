using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Controls;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Where HEAD was before a write, for the follow step to compare against once it is over.
/// </summary>
/// <param name="HeadSha">The commit, or <see langword="null"/> when there was none yet.</param>
public sealed record SubmoduleFollowMark(string? HeadSha);

/// <summary>
/// The step every write of the application ends with: the submodules whose expected commit the write
/// changed are put on it, and the ones not downloaded are downloaded.
/// </summary>
/// <remarks>
/// <para>
/// One place in the write path (<see cref="IRepositoryContext.RunExclusiveAsync"/>) rather than one call
/// per operation, so a checkout, a pull, a merge — finishing a conflicted one included — a reset, a
/// revert, a squash, and any operation added later, are covered alike. A write that does not move HEAD
/// costs nothing here, and nothing at all runs for a repository without a <c>.gitmodules</c>.
/// </para>
/// <para>
/// It never fails the write: the write is done. A submodule left behind, and a follow that could not
/// run, are reported in a warning.
/// </para>
/// </remarks>
public interface ISubmoduleFollowStep
{
    /// <summary>
    /// Notes where HEAD is before a write.
    /// </summary>
    /// <param name="repository">The repository the write is for.</param>
    /// <param name="knownHead">
    /// The commit HEAD was on at the last refresh, used — without running git — when the repository has
    /// no <c>.gitmodules</c> yet: a checkout may bring one.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The mark, or <see langword="null"/> when the setting is off and nothing will follow.</returns>
    Task<SubmoduleFollowMark?> BeforeAsync(RepositoryHandle repository, string? knownHead, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the submodules the write changed on their expected commits, behind the progress card, once
    /// the write is over and before the repository is read again.
    /// </summary>
    /// <param name="repository">The repository the write was for.</param>
    /// <param name="mark">Where HEAD was before it.</param>
    /// <param name="cancellationToken">The write's token.</param>
    /// <returns>What the follow did, or <see langword="null"/> when HEAD did not move or nothing ran.</returns>
    Task<SubmoduleUpdateResult?> AfterAsync(RepositoryHandle repository, SubmoduleFollowMark mark, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="ISubmoduleFollowStep"/>.
/// </summary>
public sealed class SubmoduleFollowStep : ISubmoduleFollowStep
{
    private readonly ISettingsService _settings;
    private readonly ISubmoduleService _submodules;
    private readonly IOverlayService _overlay;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<SubmoduleFollowStep> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="settings">Says whether submodules follow at all.</param>
    /// <param name="submodules">Reads HEAD and moves the submodules.</param>
    /// <param name="overlay">Shows the progress card while a submodule is updated.</param>
    /// <param name="infoBar">Names the submodules left behind.</param>
    /// <param name="logger">Receives a follow that could not run.</param>
    public SubmoduleFollowStep(
        ISettingsService settings,
        ISubmoduleService submodules,
        IOverlayService overlay,
        IInfoBarService infoBar,
        ILogger<SubmoduleFollowStep> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(submodules);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _submodules = submodules;
        _overlay = overlay;
        _infoBar = infoBar;
        _logger = logger;

        CancelCommand = new RelayCommand(() => _cancellation?.Cancel());
    }

    /// <summary>Gets the command the progress card's Cancel button runs.</summary>
    public RelayCommand CancelCommand { get; }

    /// <inheritdoc />
    public async Task<SubmoduleFollowMark?> BeforeAsync(
        RepositoryHandle repository,
        string? knownHead,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!_settings.Current.UpdateSubmodulesAutomatically)
        {
            return null;
        }

        if (!SubmoduleService.HasGitModules(repository.WorkTreePath))
        {
            return new SubmoduleFollowMark(knownHead is { Length: > 0 } ? knownHead : null);
        }

        try
        {
            return new SubmoduleFollowMark(await _submodules.ReadHeadAsync(repository, cancellationToken).ConfigureAwait(true));
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "HEAD of {Repository} could not be read before a write", repository);
            return new SubmoduleFollowMark(knownHead is { Length: > 0 } ? knownHead : null);
        }
    }

    /// <inheritdoc />
    public async Task<SubmoduleUpdateResult?> AfterAsync(
        RepositoryHandle repository,
        SubmoduleFollowMark mark,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(mark);

        if (!SubmoduleService.HasGitModules(repository.WorkTreePath))
        {
            return null;
        }

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;

        ProgressOverlayCard card = new()
        {
            Title = "Updating submodules",
            Message = "Reading the submodules…",
            IsIndeterminate = true,
            CancelCommand = CancelCommand,
        };

        bool shown = false;
        bool done = false;

        // The card shows only once a submodule is actually updated: a write that changed none says
        // nothing. A Progress<T> posts its reports, so one can arrive after the follow is over — it must
        // not open a card nothing would close.
        Progress<SubmoduleUpdateProgress> progress = new(report =>
        {
            if (done)
            {
                return;
            }

            if (!shown)
            {
                shown = true;
                _overlay.ShowAsync(card).Forget();
            }

            SubmoduleOperations.Apply(card, report);
        });

        try
        {
            string? after = await _submodules.ReadHeadAsync(repository, cancellation.Token).ConfigureAwait(true);

            if (after is null || string.Equals(after, mark.HeadSha, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            SubmoduleUpdateResult result = await _submodules
                .FollowAsync(repository, mark.HeadSha, after, progress, cancellation.Token)
                .ConfigureAwait(true);

            Report(result);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _infoBar.Notify(
                "Submodule update cancelled",
                "The operation is done; the submodules not updated yet stay where they were. Update them from Submodules in the toolbar.",
                InfoBarSeverity.Info);
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "The submodules of {Repository} could not follow a write", repository);

            _infoBar.Notify(
                "The submodules were not updated",
                $"{ArgumentRedactor.Redact(exception.StandardError.Trim())} Update them from Submodules in the toolbar.",
                InfoBarSeverity.Warning);
        }
        finally
        {
            _cancellation = null;
            done = true;

            if (shown)
            {
                await _overlay.HideAsync().ConfigureAwait(true);
            }
        }

        return null;
    }

    private void Report(SubmoduleUpdateResult result)
    {
        if (SubmoduleOperations.DescribeProblems(result) is not { } problems)
        {
            return;
        }

        _infoBar.Notify(
            problems.Title,
            $"{problems.Message}{Environment.NewLine}Update them from Submodules in the toolbar once that is settled.",
            InfoBarSeverity.Warning);
    }
}
