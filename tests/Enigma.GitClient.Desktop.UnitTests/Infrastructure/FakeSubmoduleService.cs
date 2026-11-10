using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="ISubmoduleService"/> that answers from memory and records what it was asked to do.
/// </summary>
public sealed class FakeSubmoduleService : ISubmoduleService
{
    /// <summary>Gets or sets the submodules every list returns.</summary>
    public IReadOnlyList<GitSubmodule> Submodules { get; set; } = [];

    /// <summary>Gets or sets what every list fails with instead, or <see langword="null"/> for a list that works.</summary>
    public Exception? ListFailure { get; set; }

    /// <summary>Gets or sets what every update returns.</summary>
    public SubmoduleUpdateResult UpdateResult { get; set; } = SubmoduleUpdateResult.Nothing;

    /// <summary>Gets the paths of every update asked for, oldest first; <see langword="null"/> for "all".</summary>
    public List<IReadOnlyCollection<string>?> Updates { get; } = [];

    /// <summary>Gets or sets the pins every <see cref="ChangedPinsAsync"/> names.</summary>
    public IReadOnlyList<string> ChangedPins { get; set; } = [];

    /// <summary>Gets the commit ranges <see cref="ChangedPinsAsync"/> was asked about, oldest first.</summary>
    public List<(string? Old, string New)> ChangedPinsAsked { get; } = [];

    /// <summary>Gets or sets what <see cref="IsOnAnyRefAsync"/> answers.</summary>
    public bool IsOnAnyRef { get; set; } = true;

    /// <summary>Gets how many lists were asked for.</summary>
    public int Lists { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<GitSubmodule>> ListAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        Lists++;

        return ListFailure is { } failure
            ? Task.FromException<IReadOnlyList<GitSubmodule>>(failure)
            : Task.FromResult(Submodules);
    }

    /// <inheritdoc />
    public Task<SubmoduleUpdateResult> UpdateAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string>? paths = null,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Updates.Add(paths is null ? null : new List<string>(paths));
        return Task.FromResult(UpdateResult);
    }

    /// <summary>Gets the follows asked for, oldest first.</summary>
    public List<(string? Old, string New)> Follows { get; } = [];

    /// <summary>Gets or sets what every follow returns.</summary>
    public SubmoduleUpdateResult FollowResult { get; set; } = SubmoduleUpdateResult.Nothing;

    /// <summary>Gets or sets the commit every HEAD read answers, one per read in turn and then the last.</summary>
    public Queue<string?> Heads { get; } = new();

    /// <inheritdoc />
    public Task<SubmoduleUpdateResult> FollowAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Follows.Add((oldSha, newSha));
        return Task.FromResult(FollowResult);
    }

    /// <inheritdoc />
    public Task<string?> ReadHeadAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => Task.FromResult(Heads.Count > 1 ? Heads.Dequeue() : Heads.Count == 1 ? Heads.Peek() : null);

    /// <summary>Gets the pin changes <see cref="DescribePinChangeAsync"/> answers, by path.</summary>
    public Dictionary<string, PinChange> PinChanges { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the commits <see cref="DescribeCommitAsync"/> answers, by object name.</summary>
    public Dictionary<string, PinCommit> Commits { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<PinCommit?> DescribeCommitAsync(RepositoryHandle repository, string path, string sha, CancellationToken cancellationToken = default)
        => Task.FromResult(Commits.TryGetValue(sha, out PinCommit? commit) ? commit : null);

    /// <summary>Gets or sets the staged pin changes every read answers.</summary>
    public IReadOnlyList<StagedPin> StagedPins { get; set; } = [];

    /// <summary>Gets or sets what every discard plan answers.</summary>
    public IReadOnlyList<SubmoduleDiscard> DiscardPlans { get; set; } = [];

    /// <summary>Gets the paths of every put-back asked for, oldest first.</summary>
    public List<IReadOnlyCollection<string>> PutBacks { get; } = [];

    /// <inheritdoc />
    public Task<PinChange> DescribePinChangeAsync(
        RepositoryHandle repository,
        string path,
        string? oldSha,
        string? newSha,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PinChanges.TryGetValue(path, out PinChange? change)
            ? change
            : new PinChange(path, oldSha ?? string.Empty, newSha ?? string.Empty, PinDirection.Unknown, [], 0, [], 0, "Not scripted."));

    /// <inheritdoc />
    public Task<IReadOnlyList<StagedPin>> ReadStagedPinsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => Task.FromResult(StagedPins);

    /// <inheritdoc />
    public Task<IReadOnlyList<SubmoduleDiscard>> PlanDiscardAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
        => Task.FromResult(DiscardPlans);

    /// <inheritdoc />
    public Task<SubmoduleUpdateResult> PutBackAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        PutBacks.Add(new List<string>(paths));
        return Task.FromResult(new SubmoduleUpdateResult([.. paths], [], []));
    }

    /// <summary>Gets or sets the superproject every read answers, or <see langword="null"/> for none.</summary>
    public string? Superproject { get; set; }

    /// <inheritdoc />
    public Task<string?> ReadSuperprojectAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => Task.FromResult(Superproject);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ChangedPinsAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken = default)
    {
        ChangedPinsAsked.Add((oldSha, newSha));
        return Task.FromResult(ChangedPins);
    }

    /// <inheritdoc />
    public Task<bool> IsOnAnyRefAsync(GitSubmodule submodule, string sha, CancellationToken cancellationToken = default)
        => Task.FromResult(IsOnAnyRef);
}
