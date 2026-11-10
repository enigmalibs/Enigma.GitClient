using System;
using System.Collections.Generic;
using System.Linq;
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
/// What the application does with a submodule: update it, open it in a window of its own, open its
/// folder or a terminal in it, copy its path.
/// </summary>
public interface ISubmoduleOperations
{
    /// <summary>
    /// Puts submodules on the commits the repository expects — downloading the ones that are not —
    /// behind the progress card, and says how it went.
    /// </summary>
    /// <param name="paths">The submodules, by path, or <see langword="null"/> for every one.</param>
    /// <returns>
    /// What the update did, or <see langword="null"/> when it did not run to its end: no repository,
    /// cancelled, or failed outright (the info bar says which).
    /// </returns>
    /// <remarks>
    /// A submodule left where it was — uncommitted changes, a commit no branch contains — or one git
    /// could not update is named in one warning, with the reason. Runs as a write, and the repository is
    /// read again after it.
    /// </remarks>
    Task<SubmoduleUpdateResult?> UpdateAsync(IReadOnlyCollection<string>? paths);

    /// <summary>
    /// Downloads the submodules of a worktree just created, behind the progress card, before its window
    /// opens — when the submodules follow automatically and the worktree declares some.
    /// </summary>
    /// <param name="workTreePath">The new worktree's folder.</param>
    /// <returns>A task that completes once they are downloaded, or at once when there is nothing to do.</returns>
    /// <remarks>
    /// A linked worktree keeps submodules of its own, which <c>git worktree add</c> leaves empty. What
    /// could not be downloaded is named in a warning; the worktree is there all the same.
    /// </remarks>
    Task PrepareNewWorktreeAsync(string workTreePath);

    /// <summary>
    /// Opens a submodule in a new window of the application.
    /// </summary>
    /// <param name="submodule">The submodule, downloaded.</param>
    /// <returns><see langword="true"/> when a window started; otherwise the info bar says so.</returns>
    Task<bool> OpenAsync(GitSubmodule submodule);

    /// <summary>
    /// Opens a submodule's folder in the file manager.
    /// </summary>
    /// <param name="submodule">The submodule.</param>
    /// <returns><see langword="true"/> when the folder opened; otherwise the info bar says so.</returns>
    Task<bool> OpenFolderAsync(GitSubmodule submodule);

    /// <summary>
    /// Opens a terminal in a submodule's folder.
    /// </summary>
    /// <param name="submodule">The submodule.</param>
    /// <returns><see langword="true"/> when a terminal started; otherwise the info bar says so.</returns>
    Task<bool> OpenTerminalAsync(GitSubmodule submodule);

    /// <summary>
    /// Copies a submodule's full path to the clipboard.
    /// </summary>
    /// <param name="submodule">The submodule.</param>
    /// <returns>A task that completes once the text is on the clipboard.</returns>
    Task CopyPathAsync(GitSubmodule submodule);
}

/// <summary>
/// Default <see cref="ISubmoduleOperations"/>.
/// </summary>
public sealed class SubmoduleOperations : ISubmoduleOperations
{
    /// <summary>The progress card's title, and the reports' first words.</summary>
    public const string UpdateTitle = "Update submodules";

    private readonly IRepositoryContext _context;
    private readonly ISubmoduleService _submodules;
    private readonly IRepositoryLocator _locator;
    private readonly ISettingsService _settings;
    private readonly IOverlayService _overlay;
    private readonly IInstanceLauncher _launcher;
    private readonly ISystemInterop _interop;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<SubmoduleOperations> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="submodules">Updates the submodules.</param>
    /// <param name="locator">Opens a new worktree, to download its submodules.</param>
    /// <param name="settings">Says whether a new worktree's submodules are downloaded.</param>
    /// <param name="overlay">Shows the progress card.</param>
    /// <param name="launcher">Opens a submodule in another instance.</param>
    /// <param name="interop">Opens a submodule's folder or a terminal in it, and copies its path.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public SubmoduleOperations(
        IRepositoryContext context,
        ISubmoduleService submodules,
        IRepositoryLocator locator,
        ISettingsService settings,
        IOverlayService overlay,
        IInstanceLauncher launcher,
        ISystemInterop interop,
        IInfoBarService infoBar,
        ILogger<SubmoduleOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(submodules);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _submodules = submodules;
        _locator = locator;
        _settings = settings;
        _overlay = overlay;
        _launcher = launcher;
        _interop = interop;
        _infoBar = infoBar;
        _logger = logger;

        CancelCommand = new RelayCommand(() => _cancellation?.Cancel());
    }

    /// <summary>Gets the command the progress card's Cancel button runs.</summary>
    public RelayCommand CancelCommand { get; }

    /// <inheritdoc />
    public async Task<SubmoduleUpdateResult?> UpdateAsync(IReadOnlyCollection<string>? paths)
    {
        if (_context.Repository is null)
        {
            return null;
        }

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_context.RepositoryLifetime);

        _cancellation = cancellation;

        ProgressOverlayCard card = new()
        {
            Title = UpdateTitle,
            Message = "Reading the submodules…",
            IsIndeterminate = true,
            CancelCommand = CancelCommand,
        };

        Progress<SubmoduleUpdateProgress> progress = new(report => Apply(card, report));

        await _overlay.ShowAsync(card).ConfigureAwait(true);

        try
        {
            SubmoduleUpdateResult result = await _context
                .RunExclusiveAsync(
                    (repository, token) => _submodules.UpdateAsync(repository, paths, progress, token),
                    refreshAfter: true,
                    cancellation.Token)
                .ConfigureAwait(true);

            Report(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            _infoBar.Notify($"{UpdateTitle} cancelled", "What was already updated stays updated.", InfoBarSeverity.Info);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "The submodules could not be updated");

            _infoBar.Notify($"{UpdateTitle} failed", FirstLine(exception.StandardError), InfoBarSeverity.Error);
        }
        finally
        {
            _cancellation = null;

            // Always: an overlay left open makes the whole window unusable.
            await _overlay.HideAsync().ConfigureAwait(true);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task PrepareNewWorktreeAsync(string workTreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        if (!_settings.Current.UpdateSubmodulesAutomatically
            || !SubmoduleService.HasGitModules(workTreePath)
            || !_context.IsRepositoryOpen)
        {
            return;
        }

        RepositoryDiscoveryResult discovered = await _locator.DiscoverAsync(workTreePath, _context.RepositoryLifetime)
            .ConfigureAwait(true);

        if (discovered.Repository is not { } worktree)
        {
            return;
        }

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_context.RepositoryLifetime);

        _cancellation = cancellation;

        ProgressOverlayCard card = new()
        {
            Title = "Downloading the worktree's submodules",
            Message = "Reading the submodules…",
            IsIndeterminate = true,
            CancelCommand = CancelCommand,
        };

        Progress<SubmoduleUpdateProgress> progress = new(report => Apply(card, report));

        await _overlay.ShowAsync(card).ConfigureAwait(true);

        try
        {
            // Serialised with this window's writes: the worktree shares the repository's objects.
            SubmoduleUpdateResult result = await _context
                .RunExclusiveAsync(
                    (_, token) => _submodules.UpdateAsync(worktree, null, progress, token),
                    refreshAfter: false,
                    cancellation.Token,
                    followSubmodules: false)
                .ConfigureAwait(true);

            if (DescribeProblems(result) is { } problems)
            {
                _infoBar.Notify(problems.Title, problems.Message, InfoBarSeverity.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _infoBar.Notify(
                "Submodule download cancelled",
                $"The worktree is in '{workTreePath}'; its submodules not downloaded yet can be from its own window.",
                InfoBarSeverity.Info);
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "The submodules of the worktree {Folder} could not be downloaded", workTreePath);
            _infoBar.Notify("The worktree's submodules were not downloaded", FirstLine(exception.StandardError), InfoBarSeverity.Warning);
        }
        finally
        {
            _cancellation = null;
            await _overlay.HideAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> OpenAsync(GitSubmodule submodule)
    {
        ArgumentNullException.ThrowIfNull(submodule);

        if (_launcher.Launch(submodule.FullPath))
        {
            return Task.FromResult(true);
        }

        _infoBar.Notify(
            "No new window",
            $"Another instance of Enigma.GitClient could not be started to open '{submodule.FullPath}'.",
            InfoBarSeverity.Error);

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<bool> OpenFolderAsync(GitSubmodule submodule)
    {
        ArgumentNullException.ThrowIfNull(submodule);

        if (await _interop.OpenPathAsync(submodule.FullPath).ConfigureAwait(true))
        {
            return true;
        }

        _infoBar.Notify(
            "The folder did not open",
            $"Nothing on this desktop opened '{submodule.FullPath}'.",
            InfoBarSeverity.Warning);

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> OpenTerminalAsync(GitSubmodule submodule)
    {
        ArgumentNullException.ThrowIfNull(submodule);

        if (await _interop.OpenTerminalAsync(submodule.FullPath).ConfigureAwait(true))
        {
            return true;
        }

        _infoBar.Notify(
            "No terminal started",
            $"No terminal could be started in '{submodule.FullPath}'.",
            InfoBarSeverity.Warning);

        return false;
    }

    /// <inheritdoc />
    public Task CopyPathAsync(GitSubmodule submodule)
    {
        ArgumentNullException.ThrowIfNull(submodule);

        return _interop.CopyTextAsync(submodule.FullPath);
    }

    /// <summary>
    /// Builds the warning that names what an update left behind, or <see langword="null"/> when it
    /// left nothing.
    /// </summary>
    /// <param name="result">What the update did.</param>
    /// <returns>The title and the message, one line per submodule.</returns>
    public static (string Title, string Message)? DescribeProblems(SubmoduleUpdateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.HasProblems)
        {
            return null;
        }

        IEnumerable<string> lines = result.Skipped
            .Select(skip => skip.Message)
            .Concat(result.Failed.Select(failure => $"{failure.Path} could not be updated: {failure.Message}"));

        int count = result.Skipped.Count + result.Failed.Count;
        string title = count == 1 ? "A submodule was not updated" : $"{count} submodules were not updated";

        return (title, string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// Moves the progress card to what the update last said.
    /// </summary>
    /// <param name="card">The card on screen.</param>
    /// <param name="report">What the update said.</param>
    public static void Apply(ProgressOverlayCard card, SubmoduleUpdateProgress report)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(report);

        card.Message = report.Description;
        card.IsIndeterminate = !report.Step.IsDeterminate;

        if (report.Step.IsDeterminate)
        {
            card.Progress = report.Step.Percent!.Value;
        }
    }

    private void Report(SubmoduleUpdateResult result)
    {
        if (DescribeProblems(result) is { } problems)
        {
            _infoBar.Notify(problems.Title, problems.Message, InfoBarSeverity.Warning);
            return;
        }

        string message = result.Updated.Count switch
        {
            0 => "Every submodule was already on the commit this repository expects.",
            1 => $"{result.Updated[0]} is on the commit this repository expects.",
            _ => $"{result.Updated.Count} submodules are on the commits this repository expects.",
        };

        _infoBar.Notify("Submodules updated", message, InfoBarSeverity.Success);
    }

    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n', '\r'))
        {
            if (line.Trim() is { Length: > 0 } trimmed)
            {
                return ArgumentRedactor.Redact(trimmed);
            }
        }

        return "git could not read the submodules.";
    }
}
