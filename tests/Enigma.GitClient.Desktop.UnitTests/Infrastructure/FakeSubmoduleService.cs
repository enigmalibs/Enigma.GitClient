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
