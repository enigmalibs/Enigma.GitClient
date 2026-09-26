using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Tags;

/// <summary>
/// Creates and removes tags.
/// </summary>
/// <remarks>
/// A tag is either lightweight — a name pointing straight at a commit — or annotated, a real object
/// carrying a tagger and a message. Which one is created is decided by whether a message was
/// written, because that is the only difference a user cares about: "I want to say something about
/// this release" is exactly when an annotated tag is the right one.
/// </remarks>
public interface ITagService
{
    /// <summary>
    /// Creates a tag.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The tag's name, without <c>refs/tags/</c>.</param>
    /// <param name="target">What the tag points at. <see langword="null"/> means HEAD.</param>
    /// <param name="message">
    /// The annotation. When given, an annotated tag object is created; when not, the tag is a plain
    /// pointer.
    /// </param>
    /// <param name="force">Whether to move a tag of this name that already exists.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the tag exists.</returns>
    Task CreateAsync(
        RepositoryHandle repository,
        string name,
        string? target = null,
        string? message = null,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a local tag.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The tag to delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the tag is gone.</returns>
    Task DeleteAsync(RepositoryHandle repository, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tag from a remote.
    /// </summary>
    /// <param name="repository">The repository to write from.</param>
    /// <param name="remote">The remote's name.</param>
    /// <param name="name">The tag's name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the remote has accepted the deletion.</returns>
    Task DeleteRemoteAsync(
        RepositoryHandle repository,
        string remote,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes one tag to a remote.
    /// </summary>
    /// <param name="repository">The repository to write from.</param>
    /// <param name="remote">The remote's name.</param>
    /// <param name="name">The tag's name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the remote has the tag.</returns>
    Task PushAsync(
        RepositoryHandle repository,
        string remote,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers whether a tag already exists.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="name">The tag's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when the tag exists.</returns>
    Task<bool> ExistsAsync(RepositoryHandle repository, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an annotated tag's whole message, body included.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="name">The tag's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The message, empty for a lightweight tag.</returns>
    /// <remarks>
    /// The reference reader carries only the subject line, because its records are newline
    /// separated and a multi-line field cannot survive that. The whole message is a second, cheap
    /// read, made only where a detail view actually shows it.
    /// </remarks>
    Task<string> GetMessageAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ITagService"/>, driving the real git executable.
/// </summary>
public sealed class TagService : ITagService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    public TagService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task CreateAsync(
        RepositoryHandle repository,
        string name,
        string? target = null,
        string? message = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        RefNameValidation validation = RefNameValidator.ValidateTag(name);

        if (!validation.IsValid)
        {
            throw new GitOperationRefusedException(validation.Message);
        }

        if (!force && await ExistsAsync(repository, name, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException($"A tag called \"{name}\" already exists.");
        }

        List<string> arguments = ["tag"];

        if (force)
        {
            arguments.Add("--force");
        }

        if (message is { Length: > 0 })
        {
            // -m makes it an annotated tag: a real object with a tagger and a message, which is
            // what a release wants. Without a message a lightweight tag is the honest choice — an
            // empty annotation would be worse than none.
            arguments.Add("--annotate");
            arguments.Add("--message");
            arguments.Add(message);
        }

        arguments.Add(name);

        if (target is { Length: > 0 })
        {
            arguments.Add(target);
        }

        await RunAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await RunAsync(repository, ["tag", "--delete", name], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRemoteAsync(
        RepositoryHandle repository,
        string remote,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // The full ref name, not the short one: "git push --delete origin v1" would happily delete a
        // branch called v1 if the tag did not exist.
        await RunAsync(repository, ["push", remote, "--delete", $"{GitTag.Prefix}{name}"], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PushAsync(
        RepositoryHandle repository,
        string remote,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await RunAsync(repository, ["push", remote, $"{GitTag.Prefix}{name}"], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["show-ref", "--verify", "--quiet", $"{GitTag.Prefix}{name}"]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<string> GetMessageAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // The object type comes first because "%(contents)" on a lightweight tag returns the
        // commit's own message, which is not the tag's annotation and must not be shown as one.
        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["tag", "--list", "--format=%(objecttype)%00%(contents)", name]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return string.Empty;
        }

        int separator = result.StandardOutput.IndexOf('\u0000', StringComparison.Ordinal);

        if (separator < 0)
        {
            return string.Empty;
        }

        string objectType = result.StandardOutput[..separator];

        return string.Equals(objectType, "tag", StringComparison.Ordinal)
            ? result.StandardOutput[(separator + 1)..].TrimEnd('\n', '\r')
            : string.Empty;
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
