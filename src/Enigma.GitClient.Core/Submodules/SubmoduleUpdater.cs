using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// The updating half of <see cref="SubmoduleService"/>: putting submodules on the commits their
/// repository expects, following a move of HEAD, and planning and doing the discard of a pin change.
/// </summary>
/// <remarks>
/// An update applies its rule at every level before it goes deeper. A submodule with uncommitted
/// changes, or on a commit no ref contains, is skipped and said, never moved.
/// </remarks>
internal sealed class SubmoduleUpdater
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IGitCredentialResolver _credentials;
    private readonly SubmoduleReader _reader;
    private readonly ILogger<SubmoduleService> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="credentials">Signs a download in with the repository's profile.</param>
    /// <param name="reader">Lists the submodules to update, level by level.</param>
    /// <param name="logger">Receives what failed and why, under the service's category.</param>
    public SubmoduleUpdater(
        IGitProcessRunner runner,
        IGitCommandFactory commandFactory,
        IGitCredentialResolver credentials,
        SubmoduleReader reader,
        ILogger<SubmoduleService> logger)
    {
        _runner = runner;
        _commandFactory = commandFactory;
        _credentials = credentials;
        _reader = reader;
        _logger = logger;
    }

    /// <inheritdoc cref="ISubmoduleService.UpdateAsync"/>
    public async Task<SubmoduleUpdateResult> UpdateAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string>? paths = null,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!SubmoduleService.HasGitModules(repository.WorkTreePath) || paths is { Count: 0 })
        {
            return SubmoduleUpdateResult.Nothing;
        }

        GitCredentials credentials = await _credentials.ForRepositoryAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        UpdateRun run = new(repository, credentials, progress);

        if (paths is null)
        {
            foreach (GitSubmodule submodule in await _reader.ListLevelAsync(repository.WorkTreePath, null, cancellationToken)
                .ConfigureAwait(false))
            {
                await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
            }

            return run.ToResult();
        }

        IReadOnlyList<GitSubmodule> listed = await _reader.ListAsync(repository, cancellationToken).ConfigureAwait(false);
        HashSet<string> requested = new(StringComparer.Ordinal);

        foreach (string path in paths)
        {
            requested.Add(SubmoduleOutputParser.NormalisePath(path ?? string.Empty));
        }

        foreach (string path in requested)
        {
            // Updating a submodule updates the ones nested in it, against the commit it moves to; the
            // state listed for a nested one before that move would be stale.
            if (HasRequestedAncestor(path, requested, listed))
            {
                continue;
            }

            GitSubmodule? submodule = Find(listed, path);

            if (submodule is null)
            {
                run.Skip(new SubmoduleSkip(path, SubmoduleSkipReason.NotASubmodule));
                continue;
            }

            await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
        }

        return run.ToResult();
    }

    /// <inheritdoc cref="ISubmoduleService.FollowAsync"/>
    public async Task<SubmoduleUpdateResult> FollowAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        SubmoduleReader.EnsureRevision(newSha, nameof(newSha));

        if (!SubmoduleService.HasGitModules(repository.WorkTreePath))
        {
            return SubmoduleUpdateResult.Nothing;
        }

        IReadOnlyList<string> changed = await _reader.ChangedPinsAsync(repository, oldSha, newSha, cancellationToken)
            .ConfigureAwait(false);

        GitCredentials credentials = await _credentials.ForRepositoryAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        UpdateRun run = new(repository, credentials, progress, followOnly: true);

        await FollowLevelAsync(run, repository.WorkTreePath, parent: null, changed, cancellationToken).ConfigureAwait(false);

        return run.ToResult();
    }

    /// <inheritdoc cref="ISubmoduleService.PlanDiscardAsync"/>
    public async Task<IReadOnlyList<SubmoduleDiscard>> PlanDiscardAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(paths);

        List<SubmoduleDiscard> plans = [];

        if (paths.Count == 0 || !SubmoduleService.HasGitModules(repository.WorkTreePath))
        {
            return plans;
        }

        List<string> arguments = ["--literal-pathspecs", "ls-tree", "-z", "HEAD", "--"];
        arguments.AddRange(paths);

        GitResult tree = await _runner.RunAsync(_commandFactory.Create(repository.WorkTreePath, arguments), throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> headPins = new(StringComparer.Ordinal);

        if (tree.IsSuccess)
        {
            foreach ((string path, string sha) in SubmoduleOutputParser.ParseTreeGitlinkEntries(tree.StandardOutput))
            {
                headPins[path] = sha;
            }
        }

        if (headPins.Count == 0)
        {
            return plans;
        }

        HashSet<string> wanted = new(headPins.Keys, StringComparer.Ordinal);

        foreach (GitSubmodule submodule in await _reader.ListLevelAsync(repository.WorkTreePath, null, cancellationToken, wanted).ConfigureAwait(false))
        {
            if (!headPins.TryGetValue(submodule.Path, out string? headSha))
            {
                continue;
            }

            int leftBehind = submodule.CheckedOutSha is { } checkedOut && !string.Equals(checkedOut, headSha, StringComparison.OrdinalIgnoreCase)
                ? await _reader.CountCommitsAsync(submodule.FullPath, [checkedOut, "--not", "--branches", "--tags", "--remotes"], cancellationToken).ConfigureAwait(false)
                : 0;

            plans.Add(new SubmoduleDiscard(submodule.Path, headSha, submodule.CheckedOutSha, submodule.HasUncommittedChanges, leftBehind));
        }

        return plans;
    }

    /// <inheritdoc cref="ISubmoduleService.PutBackAsync"/>
    public async Task<SubmoduleUpdateResult> PutBackAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0 || !SubmoduleService.HasGitModules(repository.WorkTreePath))
        {
            return SubmoduleUpdateResult.Nothing;
        }

        GitCredentials credentials = await _credentials.ForRepositoryAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        UpdateRun run = new(repository, credentials, progress, followOnly: true) { LeavesUnreferencedCommits = true };
        HashSet<string> wanted = new(paths, StringComparer.Ordinal);

        foreach (GitSubmodule submodule in await _reader.ListLevelAsync(repository.WorkTreePath, null, cancellationToken, wanted).ConfigureAwait(false))
        {
            if (wanted.Contains(submodule.Path))
            {
                await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
            }
        }

        return run.ToResult();
    }

    private static bool HasRequestedAncestor(string path, HashSet<string> requested, IReadOnlyList<GitSubmodule> listed)
    {
        for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
        {
            string ancestor = path.Substring(0, slash);

            if (requested.Contains(ancestor) && Find(listed, ancestor) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static GitSubmodule? Find(IReadOnlyList<GitSubmodule> submodules, string path)
    {
        foreach (GitSubmodule submodule in submodules)
        {
            if (string.Equals(submodule.Path, path, StringComparison.Ordinal))
            {
                return submodule;
            }
        }

        return null;
    }

    private async Task UpdateTreeAsync(UpdateRun run, GitSubmodule submodule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (submodule.State)
        {
            case SubmoduleCheckoutState.Conflicted:
                run.Skip(new SubmoduleSkip(submodule.Path, SubmoduleSkipReason.Conflicted, submodule.CheckedOutSha));
                return;

            case SubmoduleCheckoutState.UpToDate:
                break;

            case SubmoduleCheckoutState.NotDownloaded:
                if (!await CheckOutAsync(run, submodule, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                // Everything in a submodule just downloaded is still to download.
                await UpdateNestedAsync(run, submodule, cancellationToken).ConfigureAwait(false);
                return;

            default:
                if (submodule.HasUncommittedChanges)
                {
                    run.Skip(new SubmoduleSkip(submodule.Path, SubmoduleSkipReason.UncommittedChanges, submodule.CheckedOutSha));
                }
                // With nothing checked out, no commit can be left on no branch.
                else if (!run.LeavesUnreferencedCommits
                    && submodule.CheckedOutSha is { } checkedOut
                    && !await _reader.IsOnAnyRefAsync(submodule, checkedOut, cancellationToken).ConfigureAwait(false))
                {
                    run.Skip(new SubmoduleSkip(submodule.Path, SubmoduleSkipReason.CommitOnNoBranch, submodule.CheckedOutSha));
                }
                else if (!await CheckOutAsync(run, submodule, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                else if (run.FollowOnly)
                {
                    // Moved: the nested ones follow what this move changed for them.
                    IReadOnlyList<string> moved = await _reader.ChangedPinsInAsync(
                            submodule.FullPath,
                            submodule.CheckedOutSha,
                            submodule.ExpectedSha,
                            cancellationToken)
                        .ConfigureAwait(false);

                    await FollowLevelAsync(run, submodule.FullPath, submodule, moved, cancellationToken).ConfigureAwait(false);
                    return;
                }

                break;
        }

        if (run.FollowOnly)
        {
            // Not moved: nothing changed for the nested ones but those still to download.
            await FollowLevelAsync(run, submodule.FullPath, submodule, [], cancellationToken).ConfigureAwait(false);
            return;
        }

        await UpdateNestedAsync(run, submodule, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates every submodule nested in one, listed again: after a move, their expected commits are the
    /// new commit's.
    /// </summary>
    private async Task UpdateNestedAsync(UpdateRun run, GitSubmodule submodule, CancellationToken cancellationToken)
    {
        foreach (GitSubmodule nested in await _reader.ListLevelAsync(submodule.FullPath, submodule, cancellationToken)
            .ConfigureAwait(false))
        {
            await UpdateTreeAsync(run, nested, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Follows one repository level: its submodules whose recorded commit moved, and the ones not
    /// downloaded.
    /// </summary>
    private async Task FollowLevelAsync(
        UpdateRun run,
        string directory,
        GitSubmodule? parent,
        IReadOnlyCollection<string> moved,
        CancellationToken cancellationToken)
    {
        HashSet<string> paths = new(moved, StringComparer.Ordinal);

        foreach (GitSubmodule submodule in await _reader.ListLevelAsync(directory, parent, cancellationToken, paths)
            .ConfigureAwait(false))
        {
            string local = parent is null ? submodule.Path : submodule.Path[(parent.Path.Length + 1)..];

            if (paths.Contains(local) || submodule.State == SubmoduleCheckoutState.NotDownloaded)
            {
                await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs <c>git submodule update</c> for one submodule, from the repository it belongs to.
    /// </summary>
    /// <returns>Whether git succeeded.</returns>
    private async Task<bool> CheckOutAsync(UpdateRun run, GitSubmodule submodule, CancellationToken cancellationToken)
    {
        string directory;
        string local;

        if (submodule.ParentPath is null)
        {
            directory = run.Repository.WorkTreePath;
            local = submodule.Path;
        }
        else
        {
            directory = Path.GetFullPath(Path.Combine(run.Repository.WorkTreePath, submodule.ParentPath));
            local = submodule.Path.Substring(submodule.ParentPath.Length + 1);
        }

        run.Progress?.Report(SubmoduleUpdateProgress.Starting(submodule.Path));

        GitCommand command = _commandFactory
            .Create(directory, "submodule", "update", "--init", "--checkout", "--progress", "--", local)
            .WithCredentials(run.Credentials);

        ProgressRelay? relay = run.Progress is null ? null : new ProgressRelay(submodule.Path, run.Progress);

        GitResult result = await _runner.RunStreamingAsync(command, relay, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            run.Updated.Add(submodule.Path);
            return true;
        }

        string message = SubmoduleOutputParser.SummariseFailure(result.StandardError);
        _logger.LogWarning("The submodule {Path} could not be updated: {Message}", submodule.Path, message);
        run.Failed.Add(new SubmoduleFailure(submodule.Path, message));

        return false;
    }

    /// <summary>
    /// What one update has done so far.
    /// </summary>
    private sealed class UpdateRun
    {
        public UpdateRun(
            RepositoryHandle repository,
            GitCredentials credentials,
            IProgress<SubmoduleUpdateProgress>? progress,
            bool followOnly = false)
        {
            Repository = repository;
            Credentials = credentials;
            Progress = progress;
            FollowOnly = followOnly;
        }

        /// <summary>
        /// Gets a value indicating whether nested submodules are updated only where their parent's move
        /// changed them (a follow), rather than all of them (an update).
        /// </summary>
        public bool FollowOnly { get; }

        /// <summary>
        /// Gets a value indicating whether a submodule on a commit no ref contains is moved all the same —
        /// an explicit discard, whose question said so.
        /// </summary>
        public bool LeavesUnreferencedCommits { get; init; }

        public RepositoryHandle Repository { get; }

        public GitCredentials Credentials { get; }

        public IProgress<SubmoduleUpdateProgress>? Progress { get; }

        public List<string> Updated { get; } = [];

        public List<SubmoduleSkip> Skipped { get; } = [];

        public List<SubmoduleFailure> Failed { get; } = [];

        public void Skip(SubmoduleSkip skip) => Skipped.Add(skip);

        public SubmoduleUpdateResult ToResult() => new(Updated, Skipped, Failed);
    }

    /// <summary>
    /// Turns the chunks git writes into progress reports for one submodule.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="IProgress{T}"/>, not a <see cref="Progress{T}"/>, for the reason
    /// <see cref="SyncService"/> gives: marshalling belongs to the caller.
    /// </remarks>
    private sealed class ProgressRelay : IProgress<string>
    {
        private readonly string _path;
        private readonly IProgress<SubmoduleUpdateProgress> _target;

        public ProgressRelay(string path, IProgress<SubmoduleUpdateProgress> target)
        {
            _path = path;
            _target = target;
        }

        public void Report(string value)
        {
            if (SyncProgressParser.Parse(value) is { } parsed)
            {
                _target.Report(new SubmoduleUpdateProgress(_path, parsed));
            }
        }
    }
}
