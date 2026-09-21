using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Sync;

/// <summary>
/// What to push, and how.
/// </summary>
public sealed record PushRequest
{
    /// <summary>
    /// Gets the remote to push to.
    /// </summary>
    public string Remote { get; init; } = GitRemote.DefaultName;

    /// <summary>
    /// Gets the branch to push. <see langword="null"/> pushes the current one.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Gets a value indicating whether the branch's upstream is set to what it was pushed to.
    /// </summary>
    public bool SetUpstream { get; init; }

    /// <summary>
    /// Gets a value indicating whether tags are pushed along with the branch.
    /// </summary>
    public bool PushTags { get; init; }

    /// <summary>
    /// Gets a value indicating whether the push may replace commits on the remote, provided they
    /// are the ones last fetched.
    /// </summary>
    /// <remarks>
    /// This is <c>--force-with-lease</c>, never a bare <c>--force</c>. The lease is what refuses to
    /// clobber work that arrived since the last fetch — the exact case a bare force destroys, and
    /// the only reason force is offered at all.
    /// </remarks>
    public bool ForceWithLease { get; init; }

    /// <summary>
    /// Gets a value indicating whether this deletes the branch on the remote instead of publishing
    /// it.
    /// </summary>
    public bool Delete { get; init; }
}

/// <summary>
/// How a pull behaves when the branches have diverged.
/// </summary>
public enum PullStrategy
{
    /// <summary>Make a merge commit. Never a rebase — the client refuses that outright.</summary>
    Merge,

    /// <summary>Only move forward; refuse when a merge would be needed.</summary>
    FastForwardOnly,
}

/// <summary>
/// Talks to remotes: fetch, pull and push.
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Fetches from a remote, or from all of them.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="remote">The remote's name, or <see langword="null"/> for all of them.</param>
    /// <param name="prune">Whether to drop remote-tracking branches the remote no longer has.</param>
    /// <param name="fetchTags">Whether to fetch tags too.</param>
    /// <param name="progress">Receives git's own progress reports.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>A task that completes once the fetch is done.</returns>
    Task FetchAsync(
        RepositoryHandle repository,
        string? remote = null,
        bool prune = true,
        bool fetchTags = true,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls into the current branch.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="remote">The remote's name, or <see langword="null"/> for the branch's upstream.</param>
    /// <param name="branch">The remote branch, or <see langword="null"/> for the upstream's.</param>
    /// <param name="strategy">What to do when the branches have diverged.</param>
    /// <param name="progress">Receives git's own progress reports.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>A task that completes once the pull is done.</returns>
    Task PullAsync(
        RepositoryHandle repository,
        string? remote = null,
        string? branch = null,
        PullStrategy strategy = PullStrategy.Merge,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes to a remote.
    /// </summary>
    /// <param name="repository">The repository to write from.</param>
    /// <param name="request">What to push, and how.</param>
    /// <param name="progress">Receives git's own progress reports.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>A task that completes once the remote has accepted the push.</returns>
    Task PushAsync(
        RepositoryHandle repository,
        PushRequest request,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISyncService"/>.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands and streams their progress.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    public SyncService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <summary>
    /// Builds the argument vector for a fetch.
    /// </summary>
    /// <param name="remote">The remote's name, or <see langword="null"/> for all of them.</param>
    /// <param name="prune">Whether to drop remote-tracking branches the remote no longer has.</param>
    /// <param name="fetchTags">Whether to fetch tags too.</param>
    /// <returns>The arguments.</returns>
    public static List<string> BuildFetchArguments(string? remote, bool prune, bool fetchTags)
    {
        List<string> arguments = ["fetch", "--progress"];

        if (prune)
        {
            arguments.Add("--prune");
        }

        arguments.Add(fetchTags ? "--tags" : "--no-tags");

        if (remote is { Length: > 0 })
        {
            arguments.Add(remote);
        }
        else
        {
            arguments.Add("--all");
        }

        return arguments;
    }

    /// <summary>
    /// Builds the argument vector for a pull.
    /// </summary>
    /// <param name="remote">The remote's name, or <see langword="null"/> for the upstream.</param>
    /// <param name="branch">The remote branch, or <see langword="null"/> for the upstream's.</param>
    /// <param name="strategy">What to do when the branches have diverged.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <c>--no-rebase</c> is always present. A repository configured with <c>pull.rebase=true</c>
    /// would otherwise rebase, which this client does not do — and the command factory refuses the
    /// verb outright, so the alternative is not a rebase but a failure the user cannot explain.
    /// </remarks>
    public static List<string> BuildPullArguments(string? remote, string? branch, PullStrategy strategy)
    {
        List<string> arguments = ["pull", "--progress", "--no-rebase"];

        if (strategy == PullStrategy.FastForwardOnly)
        {
            arguments.Add("--ff-only");
        }

        if (remote is { Length: > 0 })
        {
            arguments.Add(remote);

            if (branch is { Length: > 0 })
            {
                arguments.Add(branch);
            }
        }

        return arguments;
    }

    /// <summary>
    /// Builds the argument vector for a push.
    /// </summary>
    /// <param name="request">What to push, and how.</param>
    /// <returns>The arguments.</returns>
    public static List<string> BuildPushArguments(PushRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> arguments = ["push", "--progress"];

        if (request.SetUpstream && !request.Delete)
        {
            arguments.Add("--set-upstream");
        }

        if (request.ForceWithLease && !request.Delete)
        {
            // Never a bare --force: the lease is the whole point.
            arguments.Add("--force-with-lease");
        }

        if (request.PushTags && !request.Delete)
        {
            arguments.Add("--follow-tags");
        }

        if (request.Delete)
        {
            arguments.Add("--delete");
        }

        arguments.Add(request.Remote.Length > 0 ? request.Remote : GitRemote.DefaultName);

        if (request.Branch is { Length: > 0 })
        {
            arguments.Add(request.Branch);
        }

        return arguments;
    }

    /// <inheritdoc />
    public Task FetchAsync(
        RepositoryHandle repository,
        string? remote = null,
        bool prune = true,
        bool fetchTags = true,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RunAsync(repository, BuildFetchArguments(remote, prune, fetchTags), progress, cancellationToken);

    /// <inheritdoc />
    public Task PullAsync(
        RepositoryHandle repository,
        string? remote = null,
        string? branch = null,
        PullStrategy strategy = PullStrategy.Merge,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RunAsync(repository, BuildPullArguments(remote, branch, strategy), progress, cancellationToken);

    /// <inheritdoc />
    public Task PushAsync(
        RepositoryHandle repository,
        PushRequest request,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Delete && request.Branch is not { Length: > 0 })
        {
            throw new SyncException(new SyncFailure(
                SyncFailureKind.Unknown,
                "Name the branch to delete on the remote.",
                string.Empty));
        }

        return RunAsync(repository, BuildPushArguments(request), progress, cancellationToken);
    }

    private async Task RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        ParsedChunks? chunks = progress is null ? null : new ParsedChunks(progress);

        GitResult result = await _runner
            .RunStreamingAsync(command, chunks, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new SyncException(SyncErrorMapper.Map(result.StandardError, result.StandardOutput));
        }
    }

    /// <summary>
    /// Turns the chunks git writes into progress reports for the caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// git rewrites its progress line with carriage returns, so each chunk is one state of that line
    /// rather than a new message.
    /// </para>
    /// <para>
    /// Deliberately a plain <see cref="IProgress{T}"/> and not a <see cref="Progress{T}"/>. A
    /// <c>Progress&lt;T&gt;</c> does not run its callback where <c>Report</c> was called: it posts to
    /// the synchronisation context captured when it was <em>constructed</em>, and queues to the thread
    /// pool when there is none. The reader reports from a thread-pool thread, so wrapping it here sent
    /// every chunk through a queue — reports could arrive after the transfer had finished, and two of
    /// them could arrive the wrong way round, which for a progress line means an overlay going
    /// backwards. Marshalling belongs to the caller, which has its own <c>Progress&lt;T&gt;</c> built
    /// where its updates must land; this only has to translate a chunk and pass it on.
    /// </para>
    /// </remarks>
    private sealed class ParsedChunks : IProgress<string>
    {
        private readonly IProgress<SyncProgress> _target;

        public ParsedChunks(IProgress<SyncProgress> target) => _target = target;

        public void Report(string value)
        {
            if (SyncProgressParser.Parse(value) is { } parsed)
            {
                _target.Report(parsed);
            }
        }
    }
}

/// <summary>
/// Manages the remotes a repository knows about.
/// </summary>
public interface IRemoteService
{
    /// <summary>
    /// Lists the remotes.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The remotes, in the order git listed them.</returns>
    Task<IReadOnlyList<GitRemote>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a remote.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The remote's name.</param>
    /// <param name="url">Its URL.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the remote exists.</returns>
    Task AddAsync(
        RepositoryHandle repository,
        string name,
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a remote, moving its tracking branches with it.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="oldName">The remote's current name.</param>
    /// <param name="newName">The name it should have.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the remote is renamed.</returns>
    Task RenameAsync(
        RepositoryHandle repository,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a remote and its tracking branches.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The remote's name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the remote is gone.</returns>
    Task RemoveAsync(RepositoryHandle repository, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a remote's URLs.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The remote's name.</param>
    /// <param name="fetchUrl">The URL to fetch from.</param>
    /// <param name="pushUrl">
    /// The URL to push to, when it differs. <see langword="null"/> makes pushes use the fetch URL.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the URLs are set.</returns>
    Task SetUrlAsync(
        RepositoryHandle repository,
        string name,
        string fetchUrl,
        string? pushUrl = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IRemoteService"/>.
/// </summary>
public sealed class RemoteService : IRemoteService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IRemoteReader _reader;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="reader">Reads the remotes back.</param>
    public RemoteService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IRemoteReader reader)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(reader);

        _runner = runner;
        _commandFactory = commandFactory;
        _reader = reader;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GitRemote>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
        => _reader.GetRemotesAsync(repository, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(
        RepositoryHandle repository,
        string name,
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        Require(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        RemoteUrlValidation validation = RemoteUrlValidator.Validate(url);

        if (!validation.IsValid)
        {
            throw new GitOperationRefusedException(validation.Message);
        }

        await RunAsync(repository, ["remote", "add", name, url], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameAsync(
        RepositoryHandle repository,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);

        Require(newName);

        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }

        await RunAsync(repository, ["remote", "rename", oldName, newName], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await RunAsync(repository, ["remote", "remove", name], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetUrlAsync(
        RepositoryHandle repository,
        string name,
        string fetchUrl,
        string? pushUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fetchUrl);

        RemoteUrlValidation validation = RemoteUrlValidator.Validate(fetchUrl);

        if (!validation.IsValid)
        {
            throw new GitOperationRefusedException(validation.Message);
        }

        await RunAsync(repository, ["remote", "set-url", name, fetchUrl], cancellationToken).ConfigureAwait(false);

        if (pushUrl is { Length: > 0 })
        {
            await RunAsync(repository, ["remote", "set-url", "--push", name, pushUrl], cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Clearing a push URL means making pushes use the fetch one, which is what setting it to
            // the fetch URL does. There is no "unset" that leaves the remote usable.
            await RunAsync(repository, ["remote", "set-url", "--push", name, fetchUrl], cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks a remote's name against the rules git applies to it.
    /// </summary>
    /// <param name="name">The proposed name.</param>
    private static void Require(string? name)
    {
        RefNameValidation validation = RefNameValidator.Validate(name, "remote");

        if (!validation.IsValid)
        {
            throw new GitOperationRefusedException(validation.Message);
        }
    }

    private async Task RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }
}
