using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;

namespace Enigma.GitClient.Core.Commits;

/// <summary>
/// What to commit and how.
/// </summary>
public sealed record CommitRequest
{
    /// <summary>
    /// Gets the commit message, subject and body.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether this replaces the current tip rather than adding to it.
    /// </summary>
    public bool Amend { get; init; }

    /// <summary>
    /// Gets a value indicating whether a commit with nothing staged is allowed.
    /// </summary>
    public bool AllowEmpty { get; init; }

    /// <summary>
    /// Gets a value indicating whether to append a <c>Signed-off-by</c> trailer.
    /// </summary>
    public bool SignOff { get; init; }

    /// <summary>
    /// Gets an author to record instead of the configured one, as <c>Name &lt;email&gt;</c>.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets a value indicating whether every work-tree change is staged first.
    /// </summary>
    public bool StageEverythingFirst { get; init; }
}

/// <summary>
/// Records commits.
/// </summary>
public interface ICommitService
{
    /// <summary>
    /// Commits what is staged.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="request">What to commit and how.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The new commit's full SHA.</returns>
    Task<string> CommitAsync(
        RepositoryHandle repository,
        CommitRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the message of the commit HEAD points at, for prefilling an amend.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The message, empty when the repository has no commits.</returns>
    Task<string> GetLastCommitMessageAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ICommitService"/>.
/// </summary>
/// <remarks>
/// The message travels through a temporary file rather than <c>-m</c>. An argument vector has a
/// length limit on both platforms and a commit message legitimately runs to pages; a file also
/// keeps the bytes exactly as they were written, which matters for anything outside ASCII.
/// </remarks>
public sealed class CommitService : ICommitService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IStatusService _status;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="status">Answers whether there is anything staged.</param>
    public CommitService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(status);

        _runner = runner;
        _commandFactory = commandFactory;
        _status = status;
    }

    /// <inheritdoc />
    public async Task<string> CommitAsync(
        RepositoryHandle repository,
        CommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Message.Trim().Length == 0)
        {
            throw new GitOperationRefusedException("Write a commit message first.");
        }

        WorkingTreeStatus status = await _status
            .GetStatusAsync(repository, includeIgnored: false, cancellationToken)
            .ConfigureAwait(false);

        if (status.HasConflicts)
        {
            throw new GitOperationRefusedException(
                "This repository has unresolved conflicts. Resolve them before committing.");
        }

        if (!request.AllowEmpty && !request.Amend && !request.StageEverythingFirst && status.Staged.Count == 0)
        {
            throw new GitOperationRefusedException(
                "Nothing is staged. Stage the changes you want in this commit first.");
        }

        string messageFile = Path.Combine(Path.GetTempPath(), $"enigma-commit-{Guid.NewGuid():N}.txt");

        try
        {
            // UTF-8 with no byte-order mark: a BOM would end up as the first characters of the
            // subject line.
            await File.WriteAllTextAsync(messageFile, Normalise(request.Message), new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

            List<string> arguments = ["commit", "--file", messageFile];

            if (request.StageEverythingFirst)
            {
                arguments.Add("--all");
            }

            if (request.Amend)
            {
                arguments.Add("--amend");
            }

            if (request.AllowEmpty)
            {
                arguments.Add("--allow-empty");
            }

            if (request.SignOff)
            {
                arguments.Add("--signoff");
            }

            if (request.Author is { Length: > 0 })
            {
                arguments.Add($"--author={request.Author}");
            }

            GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

            await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(messageFile);
        }

        GitCommand head = _commandFactory.Create(repository.WorkTreePath, ["rev-parse", "HEAD"]);
        GitResult resolved = await _runner.RunAsync(head, throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        return resolved.TrimmedOutput;
    }

    /// <inheritdoc />
    public async Task<string> GetLastCommitMessageAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["log", "-1", "--format=%B"]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? result.StandardOutput.TrimEnd('\n', '\r') : string.Empty;
    }

    /// <summary>
    /// Normalises a message to the form git records: line endings as LF, no trailing blank lines,
    /// and exactly one newline at the end.
    /// </summary>
    /// <param name="message">The message as it was typed.</param>
    /// <returns>The message to write.</returns>
    public static string Normalise(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string unified = message.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return unified.TrimEnd('\n') + "\n";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A temporary file that outlives the process is not worth failing a commit over.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
