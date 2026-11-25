using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// Default <see cref="ISubmoduleService"/>, driving the real git executable.
/// </summary>
/// <remarks>
/// <para>
/// The list is read one repository at a time — the repository, then each downloaded submodule — rather
/// than with <c>git submodule status --recursive</c>, so each level's names and addresses come from its
/// own <c>.gitmodules</c>, and so an update can apply its rule at every level before it goes deeper.
/// </para>
/// <para>
/// The work is shared by three parts, one per reason to change: reading (<see cref="SubmoduleReader"/>),
/// the pins (<see cref="SubmodulePins"/>) and updating (<see cref="SubmoduleUpdater"/>). The pure
/// parsing of git's output is <see cref="SubmoduleOutputParser"/>'s.
/// </para>
/// </remarks>
public sealed class SubmoduleService : ISubmoduleService
{
    /// <summary>The file a repository declares its submodules in.</summary>
    public const string GitModulesFileName = ".gitmodules";

    private readonly SubmoduleReader _reader;
    private readonly SubmodulePins _pins;
    private readonly SubmoduleUpdater _updater;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="credentials">Signs a download in with the repository's profile.</param>
    /// <param name="logger">Receives what was skipped and why.</param>
    public SubmoduleService(
        IGitProcessRunner runner,
        IGitCommandFactory commandFactory,
        IGitCredentialResolver credentials,
        ILogger<SubmoduleService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = new SubmoduleReader(runner, commandFactory, logger);
        _pins = new SubmodulePins(runner, commandFactory, _reader);
        _updater = new SubmoduleUpdater(runner, commandFactory, credentials, _reader, logger);
    }

    /// <summary>
    /// Tells whether a work tree declares submodules — the check that keeps everything here free for a
    /// repository without any.
    /// </summary>
    /// <param name="workTreePath">The work tree's folder.</param>
    /// <returns>Whether it holds a <c>.gitmodules</c> file.</returns>
    public static bool HasGitModules(string workTreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        return File.Exists(Path.Combine(workTreePath, GitModulesFileName));
    }

    /// <summary>How many commits a <see cref="PinChange"/> lists on each side.</summary>
    public const int PinCommitsShown = 10;

    /// <inheritdoc />
    public Task<IReadOnlyList<GitSubmodule>> ListAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => _reader.ListAsync(repository, cancellationToken);

    /// <inheritdoc />
    public Task<SubmoduleUpdateResult> UpdateAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string>? paths = null,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _updater.UpdateAsync(repository, paths, progress, cancellationToken);

    /// <inheritdoc />
    public Task<SubmoduleUpdateResult> FollowAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _updater.FollowAsync(repository, oldSha, newSha, progress, cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReadHeadAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => _reader.ReadHeadAsync(repository, cancellationToken);

    /// <inheritdoc />
    public Task<PinChange> DescribePinChangeAsync(
        RepositoryHandle repository,
        string path,
        string? oldSha,
        string? newSha,
        CancellationToken cancellationToken = default)
        => _pins.DescribePinChangeAsync(repository, path, oldSha, newSha, cancellationToken);

    /// <inheritdoc />
    public Task<PinCommit?> DescribeCommitAsync(
        RepositoryHandle repository,
        string path,
        string sha,
        CancellationToken cancellationToken = default)
        => _pins.DescribeCommitAsync(repository, path, sha, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StagedPin>> ReadStagedPinsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => _reader.ReadStagedPinsAsync(repository, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SubmoduleDiscard>> PlanDiscardAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
        => _updater.PlanDiscardAsync(repository, paths, cancellationToken);

    /// <inheritdoc />
    public Task<SubmoduleUpdateResult> PutBackAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _updater.PutBackAsync(repository, paths, progress, cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReadSuperprojectAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => _reader.ReadSuperprojectAsync(repository, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ChangedPinsAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken = default)
        => _reader.ChangedPinsAsync(repository, oldSha, newSha, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsOnAnyRefAsync(GitSubmodule submodule, string sha, CancellationToken cancellationToken = default)
        => _reader.IsOnAnyRefAsync(submodule, sha, cancellationToken);

    /// <inheritdoc cref="SubmoduleOutputParser.ParseChangedGitlinkEntries"/>
    public static IReadOnlyList<(string Path, string OldSha, string NewSha)> ParseChangedGitlinkEntries(string? output)
        => SubmoduleOutputParser.ParseChangedGitlinkEntries(output);

    /// <inheritdoc cref="SubmoduleOutputParser.ParseTreeGitlinkEntries"/>
    public static IReadOnlyList<(string Path, string Sha)> ParseTreeGitlinkEntries(string? output)
        => SubmoduleOutputParser.ParseTreeGitlinkEntries(output);

    /// <inheritdoc cref="SubmoduleOutputParser.ParsePinCommits"/>
    public static IReadOnlyList<PinCommit> ParsePinCommits(string? output)
        => SubmoduleOutputParser.ParsePinCommits(output);

    /// <inheritdoc cref="SubmoduleOutputParser.ParseChangedGitlinks"/>
    public static IReadOnlyList<string> ParseChangedGitlinks(string? output)
        => SubmoduleOutputParser.ParseChangedGitlinks(output);

    /// <inheritdoc cref="SubmoduleOutputParser.ParseGitlinksInTree"/>
    public static IReadOnlyList<string> ParseGitlinksInTree(string? output)
        => SubmoduleOutputParser.ParseGitlinksInTree(output);

    /// <inheritdoc cref="SubmoduleOutputParser.SummariseFailure"/>
    public static string SummariseFailure(string? standardError)
        => SubmoduleOutputParser.SummariseFailure(standardError);
}
