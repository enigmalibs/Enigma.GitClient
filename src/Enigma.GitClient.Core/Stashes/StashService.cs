using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Stashes;

/// <summary>
/// One entry on the stash.
/// </summary>
/// <param name="Index">Its position, where 0 is the most recent.</param>
/// <param name="Sha">The commit the entry is recorded as.</param>
/// <param name="Message">What it says about itself.</param>
/// <param name="Branch">The branch it was made on, empty when git did not say.</param>
/// <param name="When">When it was made.</param>
public sealed record StashEntry(int Index, string Sha, string Message, string Branch, DateTimeOffset When)
{
    /// <summary>
    /// Gets the reference that names the entry, which every stash command takes.
    /// </summary>
    public string Reference => $"stash@{{{Index.ToString(CultureInfo.InvariantCulture)}}}";

    /// <summary>
    /// Gets the entry's short hash, for a list that shows one.
    /// </summary>
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    /// <inheritdoc />
    public override string ToString() => $"{Reference}: {Message}";
}

/// <summary>
/// Manages the stash: putting work aside and getting it back.
/// </summary>
public interface IStashService
{
    /// <summary>
    /// Lists the stash, most recent first.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<StashEntry>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the working tree's changes aside.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="message">What the entry should say about itself.</param>
    /// <param name="includeUntracked">Whether untracked files go with it.</param>
    /// <param name="keepIndex">Whether what is staged stays staged.</param>
    /// <param name="paths">Only these paths, or <see langword="null"/> for everything.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when something was stashed.</returns>
    Task<bool> PushAsync(
        RepositoryHandle repository,
        string? message = null,
        bool includeUntracked = true,
        bool keepIndex = false,
        IReadOnlyList<string>? paths = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an entry without removing it.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="index">The entry's position.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the changes are in the work tree.</returns>
    Task ApplyAsync(RepositoryHandle repository, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an entry and removes it.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="index">The entry's position.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the changes are in the work tree and the entry is gone.</returns>
    Task PopAsync(RepositoryHandle repository, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws an entry away.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="index">The entry's position.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the entry is gone.</returns>
    Task DropAsync(RepositoryHandle repository, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an entry's contents as a patch.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="index">The entry's position.</param>
    /// <param name="options">How much to parse.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The files the entry holds.</returns>
    Task<PatchSet> ShowAsync(
        RepositoryHandle repository,
        int index,
        DiffParseOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a branch from an entry and applies it there.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="index">The entry's position.</param>
    /// <param name="name">The branch's name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the branch exists with the changes on it.</returns>
    /// <remarks>
    /// This is the way out of a stash that no longer applies cleanly: git makes the branch from the
    /// commit the stash was taken on, so the changes go back exactly where they came from, and the
    /// entry is dropped once they do.
    /// </remarks>
    Task BranchAsync(
        RepositoryHandle repository,
        int index,
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IStashService"/>.
/// </summary>
public sealed class StashService : IStashService
{
    /// <summary>
    /// The format the listing is read with: the reference, the commit, the subject and the date,
    /// separated by a character no message contains.
    /// </summary>
    /// <remarks>
    /// A stash message is free text and routinely contains colons, which is what the human-readable
    /// listing separates on. The unit separator does not appear in one.
    /// </remarks>
    public const string FormatTemplate = "%gd%x1f%H%x1f%gs%x1f%aI";

    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    public StashService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <summary>
    /// Parses the listing.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <returns>The entries, in the order git listed them.</returns>
    public static IReadOnlyList<StashEntry> Parse(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        List<StashEntry> entries = [];

        foreach (string line in payload.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split('\u001f');

            if (fields.Length < 4)
            {
                continue;
            }

            entries.Add(new StashEntry(
                ParseIndex(fields[0]),
                fields[1],
                CleanMessage(fields[2], out string branch),
                branch,
                ParseDate(fields[3])));
        }

        return entries;
    }

    /// <summary>
    /// Reads the position out of a reference like <c>stash@{2}</c>.
    /// </summary>
    /// <param name="reference">The reference as git wrote it.</param>
    /// <returns>The position, or zero when it cannot be read.</returns>
    public static int ParseIndex(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        int open = reference.IndexOf('{', StringComparison.Ordinal);
        int close = reference.IndexOf('}', StringComparison.Ordinal);

        if (open < 0 || close <= open + 1)
        {
            return 0;
        }

        return int.TryParse(
            reference.AsSpan(open + 1, close - open - 1),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int index)
            ? index
            : 0;
    }

    /// <summary>
    /// Strips the boilerplate git puts on an automatic stash message, and pulls the branch out of
    /// it.
    /// </summary>
    /// <param name="subject">The reflog subject.</param>
    /// <param name="branch">Receives the branch the stash was made on, empty when unknown.</param>
    /// <returns>What the entry actually says.</returns>
    /// <remarks>
    /// git writes <c>WIP on main: 1a2b3c4 Subject</c> for a stash with no message and
    /// <c>On main: what the user typed</c> for one with. Both are worth splitting: the branch goes
    /// in its own column and the rest reads as a message.
    /// </remarks>
    public static string CleanMessage(string subject, out string branch)
    {
        ArgumentNullException.ThrowIfNull(subject);

        branch = string.Empty;

        foreach (string prefix in new[] { "WIP on ", "On " })
        {
            if (!subject.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string rest = subject[prefix.Length..];
            int colon = rest.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                return rest.Trim();
            }

            branch = rest[..colon].Trim();
            return rest[(colon + 1)..].Trim();
        }

        return subject.Trim();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StashEntry>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["stash", "list", $"--format={FormatTemplate}"]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.StandardOutput) : [];
    }

    /// <inheritdoc />
    public async Task<bool> PushAsync(
        RepositoryHandle repository,
        string? message = null,
        bool includeUntracked = true,
        bool keepIndex = false,
        IReadOnlyList<string>? paths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        List<string> arguments = ["stash", "push"];

        if (includeUntracked)
        {
            arguments.Add("--include-untracked");
        }

        if (keepIndex)
        {
            arguments.Add("--keep-index");
        }

        if (message is { Length: > 0 })
        {
            arguments.Add("--message");
            arguments.Add(message);
        }

        if (paths is { Count: > 0 })
        {
            arguments.Add("--");

            foreach (string path in paths)
            {
                if (path.Length > 0)
                {
                    arguments.Add(path);
                }
            }
        }

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);
        GitResult result = await _runner.RunAsync(command, throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        // git says so rather than failing when there was nothing to stash, and "your work is on the
        // stash" is a lie if nothing was.
        return !result.StandardOutput.Contains("No local changes to save", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public Task ApplyAsync(RepositoryHandle repository, int index, CancellationToken cancellationToken = default)
        => RunAsync(repository, ["stash", "apply", Reference(index)], cancellationToken);

    /// <inheritdoc />
    public Task PopAsync(RepositoryHandle repository, int index, CancellationToken cancellationToken = default)
        => RunAsync(repository, ["stash", "pop", Reference(index)], cancellationToken);

    /// <inheritdoc />
    public Task DropAsync(RepositoryHandle repository, int index, CancellationToken cancellationToken = default)
        => RunAsync(repository, ["stash", "drop", Reference(index)], cancellationToken);

    /// <inheritdoc />
    public async Task<PatchSet> ShowAsync(
        RepositoryHandle repository,
        int index,
        DiffParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            [
                "stash",
                "show",
                "--patch",

                // A stash made with --include-untracked records the untracked files in a third
                // parent, and "--include-untracked" is what makes them part of the patch rather
                // than invisible.
                "--include-untracked",
                "--find-renames",
                Reference(index),
            ]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? UnifiedDiffParser.Parse(result.StandardOutput, options) : PatchSet.Empty;
    }

    /// <inheritdoc />
    public async Task BranchAsync(
        RepositoryHandle repository,
        int index,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        RefNameValidation validation = RefNameValidator.ValidateBranch(name);

        if (!validation.IsValid)
        {
            throw new GitOperationRefusedException(validation.Message);
        }

        await RunAsync(repository, ["stash", "branch", name, Reference(index)], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the reference every stash command takes.
    /// </summary>
    /// <param name="index">The entry's position.</param>
    /// <returns>The reference.</returns>
    public static string Reference(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return $"stash@{{{index.ToString(CultureInfo.InvariantCulture)}}}";
    }

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private async Task RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }
}
