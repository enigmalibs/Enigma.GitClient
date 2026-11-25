using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// The pins half of <see cref="SubmoduleService"/>: what a change of a submodule's recorded commit
/// brings and takes away, and what one recorded commit is.
/// </summary>
internal sealed class SubmodulePins
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly SubmoduleReader _reader;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="reader">Counts the commits between two pins.</param>
    public SubmodulePins(IGitProcessRunner runner, IGitCommandFactory commandFactory, SubmoduleReader reader)
    {
        _runner = runner;
        _commandFactory = commandFactory;
        _reader = reader;
    }

    /// <inheritdoc cref="ISubmoduleService.DescribePinChangeAsync"/>
    public async Task<PinChange> DescribePinChangeAsync(
        RepositoryHandle repository,
        string path,
        string? oldSha,
        string? newSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string old = oldSha ?? string.Empty;
        string @new = newSha ?? string.Empty;

        PinChange Unknown(string reason) => new(path, old, @new, PinDirection.Unknown, [], 0, [], 0, reason);

        if (old.Length == 0 || @new.Length == 0)
        {
            return Unknown(old.Length == 0 ? "The submodule is added here." : "The submodule is removed here.");
        }

        if (!SubmoduleOutputParser.IsObjectName(old) || !SubmoduleOutputParser.IsObjectName(@new))
        {
            return Unknown("The commits are not whole object names.");
        }

        if (string.Equals(old, @new, StringComparison.OrdinalIgnoreCase))
        {
            return new PinChange(path, old, @new, PinDirection.Unchanged, [], 0, [], 0);
        }

        string root = Path.TrimEndingDirectorySeparator(repository.WorkTreePath);
        string folder = Path.GetFullPath(Path.Combine(root, path));

        if (!folder.StartsWith(root + Path.DirectorySeparatorChar, SubmoduleReader.PathComparison) || !SubmoduleReader.IsCheckedOut(folder))
        {
            return Unknown("The submodule is not downloaded, so its commits cannot be listed.");
        }

        foreach (string sha in new[] { old, @new })
        {
            if (!await HasCommitAsync(folder, sha, cancellationToken).ConfigureAwait(false))
            {
                return Unknown($"The submodule does not have {PinChange.Short(sha)} yet; updating it fetches it.");
            }
        }

        int ahead = await _reader.CountCommitsAsync(folder, [$"{old}..{@new}"], cancellationToken).ConfigureAwait(false);
        int behind = await _reader.CountCommitsAsync(folder, [$"{@new}..{old}"], cancellationToken).ConfigureAwait(false);

        PinDirection direction = (ahead > 0, behind > 0) switch
        {
            (true, false) => PinDirection.Forward,
            (false, true) => PinDirection.Back,
            (true, true) => PinDirection.Diverged,
            _ => PinDirection.Unchanged,
        };

        IReadOnlyList<PinCommit> added = ahead > 0
            ? await ReadCommitsAsync(folder, $"{old}..{@new}", cancellationToken).ConfigureAwait(false)
            : [];

        IReadOnlyList<PinCommit> removed = behind > 0
            ? await ReadCommitsAsync(folder, $"{@new}..{old}", cancellationToken).ConfigureAwait(false)
            : [];

        return new PinChange(path, old, @new, direction, added, ahead, removed, behind);
    }

    /// <inheritdoc cref="ISubmoduleService.DescribeCommitAsync"/>
    public async Task<PinCommit?> DescribeCommitAsync(
        RepositoryHandle repository,
        string path,
        string sha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!SubmoduleOutputParser.IsObjectName(sha))
        {
            return null;
        }

        string root = Path.TrimEndingDirectorySeparator(repository.WorkTreePath);
        string folder = Path.GetFullPath(Path.Combine(root, path));

        if (!folder.StartsWith(root + Path.DirectorySeparatorChar, SubmoduleReader.PathComparison)
            || !SubmoduleReader.IsCheckedOut(folder)
            || !await HasCommitAsync(folder, sha, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(folder, "log", "-1", "--format=%h%x1f%s%x1f%an%x1f%aI", sha, "--"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess && SubmoduleOutputParser.ParsePinCommits(result.StandardOutput) is [var commit, ..] ? commit : null;
    }

    private async Task<bool> HasCommitAsync(string directory, string sha, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(directory, "cat-file", "-e", sha + "^{commit}"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess;
    }

    private async Task<IReadOnlyList<PinCommit>> ReadCommitsAsync(string directory, string range, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(
                    directory,
                    "log",
                    $"--max-count={SubmoduleService.PinCommitsShown.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    "--format=%h%x1f%s%x1f%an%x1f%aI",
                    range,
                    "--"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? SubmoduleOutputParser.ParsePinCommits(result.StandardOutput) : [];
    }
}
