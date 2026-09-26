using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Status;

/// <summary>
/// Answers the one question about the working directory that the history view needs: is there
/// anything uncommitted?
/// </summary>
/// <remarks>
/// Deliberately narrow. The history view only needs to know whether to show its "uncommitted
/// changes" row; reading, classifying and rendering the full status is the working-directory
/// feature's job, and it builds on the same <c>status --porcelain=v2</c> output.
/// </remarks>
public interface IWorkingTreeProbe
{
    /// <summary>
    /// Checks whether the working tree or the index differs from HEAD.
    /// </summary>
    /// <param name="repository">The repository to check.</param>
    /// <param name="includeUntracked">Whether an untracked file counts as a change.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when there is something uncommitted.</returns>
    Task<bool> IsDirtyAsync(
        RepositoryHandle repository,
        bool includeUntracked = true,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IWorkingTreeProbe"/>.
/// </summary>
public sealed class WorkingTreeProbe : IWorkingTreeProbe
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the status command.</param>
    /// <param name="commandFactory">Builds the status command.</param>
    public WorkingTreeProbe(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task<bool> IsDirtyAsync(
        RepositoryHandle repository,
        bool includeUntracked = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            "status",
            "--porcelain=v2",
            "-z",
            includeUntracked ? "--untracked-files=normal" : "--untracked-files=no");

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        // A repository with no commits yet still answers, and an unreadable one is treated as clean
        // rather than blocking the history view behind an error it cannot act on.
        return result.IsSuccess && result.StandardOutput.Length > 0;
    }
}
