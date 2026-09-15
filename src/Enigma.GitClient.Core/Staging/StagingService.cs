using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Staging;

/// <summary>
/// Moves changes between the work tree and the index, and throws them away when told to.
/// </summary>
public interface IStagingService
{
    /// <summary>
    /// Stages the given paths.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="paths">The paths, relative to the work tree.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the index has them.</returns>
    Task StageAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages everything the status reported.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the index has everything.</returns>
    Task StageAllAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the given paths back out of the index, leaving the work tree alone.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="paths">The paths, relative to the work tree.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the index no longer has them.</returns>
    Task UnstageAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Empties the index back to HEAD, leaving the work tree alone.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once nothing is staged.</returns>
    Task UnstageAllAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws the given paths' changes away.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="paths">The paths, relative to the work tree.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the changes are gone.</returns>
    /// <remarks>
    /// A tracked file is restored from the index; an untracked one is deleted, because there is
    /// nothing to restore it from. Both are unrecoverable, which is why nothing here asks: the
    /// caller confirms, naming the files, before it gets this far.
    /// </remarks>
    Task DiscardAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops tracking a file, optionally leaving it on disk.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="paths">The paths, relative to the work tree.</param>
    /// <param name="keepOnDisk">Whether the file stays in the work tree.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the removal is staged.</returns>
    Task RemoveAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        bool keepOnDisk = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IStagingService"/>.
/// </summary>
public sealed class StagingService : IStagingService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    public StagingService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public Task StageAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
        => RunWithPathsAsync(repository, ["add", "--all", "--"], paths, cancellationToken);

    /// <inheritdoc />
    public Task StageAllAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return RunAsync(repository, ["add", "--all", "--", "."], cancellationToken);
    }

    /// <inheritdoc />
    public Task UnstageAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
        // "restore --staged" says what it does and, unlike "reset", never touches the work tree.
        => RunWithPathsAsync(repository, ["restore", "--staged", "--"], paths, cancellationToken);

    /// <inheritdoc />
    public Task UnstageAllAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return RunAsync(repository, ["restore", "--staged", "--", "."], cancellationToken);
    }

    /// <inheritdoc />
    public async Task DiscardAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(paths);

        List<string> tracked = [];

        foreach (string path in paths)
        {
            if (path.Length == 0)
            {
                continue;
            }

            if (await IsTrackedAsync(repository, path, cancellationToken).ConfigureAwait(false))
            {
                tracked.Add(path);
                continue;
            }

            // Nothing in git to restore an untracked file from, so discarding it means deleting it.
            string full = Path.Combine(repository.WorkTreePath, path.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(full))
            {
                File.Delete(full);
            }
            else if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        }

        if (tracked.Count > 0)
        {
            // Both sides: the work tree goes back to the index, and the index goes back to HEAD.
            await RunWithPathsAsync(
                repository,
                ["restore", "--staged", "--worktree", "--source=HEAD", "--"],
                tracked,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> paths,
        bool keepOnDisk = false,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["rm", "--quiet"];

        if (keepOnDisk)
        {
            arguments.Add("--cached");
        }

        arguments.Add("-r");
        arguments.Add("--");

        return RunWithPathsAsync(repository, arguments, paths, cancellationToken);
    }

    private async Task<bool> IsTrackedAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["ls-files", "--error-unmatch", "-z", "--", path]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private Task RunWithPathsAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> prefix,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(paths);

        List<string> arguments = [.. prefix];
        int added = 0;

        foreach (string path in paths)
        {
            if (path.Length > 0)
            {
                arguments.Add(path);
                added++;
            }
        }

        // An empty pathspec list means "everything" to several git commands, which is the opposite
        // of what an empty selection means here.
        return added == 0 ? Task.CompletedTask : RunAsync(repository, arguments, cancellationToken);
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
