using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;

namespace Enigma.GitClient.Core.Merging;

/// <summary>
/// What kind of conflict a file has.
/// </summary>
/// <remarks>
/// The two-letter state git reports says which side did what, and the seven combinations mean
/// genuinely different things to resolve: two of them have no base, two have only one side, and one
/// has nothing left at all.
/// </remarks>
public enum ConflictKind
{
    /// <summary>Both sides changed the file. The ordinary case, with all three stages.</summary>
    BothModified,

    /// <summary>Both sides added a file of the same name. There is no base.</summary>
    BothAdded,

    /// <summary>We added it; they did not have it.</summary>
    AddedByUs,

    /// <summary>They added it; we did not have it.</summary>
    AddedByThem,

    /// <summary>We deleted it; they changed it.</summary>
    DeletedByUs,

    /// <summary>They deleted it; we changed it.</summary>
    DeletedByThem,

    /// <summary>Both sides deleted it, and something else disagrees about it.</summary>
    BothDeleted,

    /// <summary>git reported a state this version does not model.</summary>
    Unknown,
}

/// <summary>
/// One file left to resolve.
/// </summary>
/// <param name="Path">Its path, relative to the work tree.</param>
/// <param name="Kind">What kind of conflict it has.</param>
/// <param name="State">The two-letter state git reported.</param>
/// <param name="IsBinary">Whether the file has no text to merge.</param>
public sealed record ConflictFile(string Path, ConflictKind Kind, string State, bool IsBinary)
{
    /// <summary>
    /// Gets a value indicating whether the file has all three stages, and so can be merged line by
    /// line rather than chosen between whole.
    /// </summary>
    public bool HasThreeWayContent => !IsBinary && Kind == ConflictKind.BothModified;

    /// <summary>
    /// Gets the sentence describing the conflict.
    /// </summary>
    public string Description
        => Kind switch
        {
            ConflictKind.BothModified => "Both sides changed this file.",
            ConflictKind.BothAdded => "Both sides added a file with this name.",
            ConflictKind.AddedByUs => "You added this file; the other side does not have it.",
            ConflictKind.AddedByThem => "The other side added this file; you do not have it.",
            ConflictKind.DeletedByUs => "You deleted this file; the other side changed it.",
            ConflictKind.DeletedByThem => "The other side deleted this file; you changed it.",
            ConflictKind.BothDeleted => "Both sides deleted this file.",
            _ => "This file has a conflict git described in a way the client does not model.",
        };

    /// <inheritdoc />
    public override string ToString() => $"{State} {Path}";
}

/// <summary>
/// The three versions of a conflicted file, as the index holds them.
/// </summary>
/// <param name="Base">Stage 1: what both sides started from, or <see langword="null"/> when there is none.</param>
/// <param name="Ours">Stage 2: our version, or <see langword="null"/> when we deleted it.</param>
/// <param name="Theirs">Stage 3: their version, or <see langword="null"/> when they deleted it.</param>
/// <param name="IsBinary">Whether any side is binary, which makes a line-level merge impossible.</param>
public sealed record ConflictSides(string? Base, string? Ours, string? Theirs, bool IsBinary)
{
    /// <summary>Gets a value indicating whether all three versions are present.</summary>
    public bool HasAllThree => Base is not null && Ours is not null && Theirs is not null;

    /// <summary>Gets a value indicating whether our side still exists.</summary>
    public bool HasOurs => Ours is not null;

    /// <summary>Gets a value indicating whether their side still exists.</summary>
    public bool HasTheirs => Theirs is not null;
}

/// <summary>
/// Reads what a conflicted merge left behind.
/// </summary>
/// <remarks>
/// Every version comes from the index stages rather than from the markers in the work-tree file.
/// That is the only way to get a real merge base — the markers do not carry one — and it is what
/// makes an exact preview possible, since the bytes are git's rather than a re-parse of them.
/// </remarks>
public interface IConflictService
{
    /// <summary>
    /// Lists the files left to resolve.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conflicted files.</returns>
    Task<IReadOnlyList<ConflictFile>> GetConflictsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a conflicted file's three versions.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The three versions, with <see langword="null"/> for any stage that does not exist.</returns>
    Task<ConflictSides> GetSidesAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IConflictService"/>.
/// </summary>
public sealed class ConflictService : IConflictService
{
    /// <summary>
    /// The index stage holding the merge base.
    /// </summary>
    public const int BaseStage = 1;

    /// <summary>
    /// The index stage holding our version.
    /// </summary>
    public const int OursStage = 2;

    /// <summary>
    /// The index stage holding their version.
    /// </summary>
    public const int TheirsStage = 3;

    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IStatusService _status;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="status">Lists the conflicted files.</param>
    public ConflictService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(status);

        _runner = runner;
        _commandFactory = commandFactory;
        _status = status;
    }

    /// <summary>
    /// Maps git's two-letter unmerged state onto a conflict kind.
    /// </summary>
    /// <param name="state">The state as <c>status --porcelain=v2</c> reported it.</param>
    /// <returns>The kind.</returns>
    public static ConflictKind ToKind(string state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state switch
        {
            "UU" => ConflictKind.BothModified,
            "AA" => ConflictKind.BothAdded,
            "AU" => ConflictKind.AddedByUs,
            "UA" => ConflictKind.AddedByThem,
            "DU" => ConflictKind.DeletedByUs,
            "UD" => ConflictKind.DeletedByThem,
            "DD" => ConflictKind.BothDeleted,
            _ => ConflictKind.Unknown,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConflictFile>> GetConflictsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        WorkingTreeStatus status = await _status
            .GetStatusAsync(repository, includeIgnored: false, cancellationToken)
            .ConfigureAwait(false);

        List<ConflictFile> files = [];

        foreach (ChangedFile file in status.Conflicted)
        {
            bool binary = await IsBinaryAsync(repository, file.Path, cancellationToken).ConfigureAwait(false);

            files.Add(new ConflictFile(file.Path, ToKind(file.ConflictState), file.ConflictState, binary));
        }

        return files;
    }

    /// <inheritdoc />
    public async Task<ConflictSides> GetSidesAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[]? baseBytes = await ReadStageAsync(repository, BaseStage, path, cancellationToken).ConfigureAwait(false);
        byte[]? ourBytes = await ReadStageAsync(repository, OursStage, path, cancellationToken).ConfigureAwait(false);
        byte[]? theirBytes = await ReadStageAsync(repository, TheirsStage, path, cancellationToken).ConfigureAwait(false);

        bool binary = LooksBinary(baseBytes) || LooksBinary(ourBytes) || LooksBinary(theirBytes);

        return new ConflictSides(
            Decode(baseBytes, binary),
            Decode(ourBytes, binary),
            Decode(theirBytes, binary),
            binary);
    }

    /// <summary>
    /// Decides whether a blob is binary the way git does: a NUL byte near the start.
    /// </summary>
    /// <param name="content">The blob, or <see langword="null"/> when the stage does not exist.</param>
    /// <returns><see langword="true"/> when there is no text to merge.</returns>
    public static bool LooksBinary(byte[]? content)
    {
        if (content is null)
        {
            return false;
        }

        int limit = Math.Min(content.Length, 8000);

        for (int index = 0; index < limit; index++)
        {
            if (content[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string? Decode(byte[]? content, bool binary)
    {
        if (content is null || binary)
        {
            return null;
        }

        // No byte-order mark is emitted and none is stripped: the bytes have to come back out
        // exactly as they went in, or the preview is not a preview of what will be written.
        return new UTF8Encoding(false).GetString(content);
    }

    private async Task<byte[]?> ReadStageAsync(
        RepositoryHandle repository,
        int stage,
        string path,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["show", $":{stage.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{path}"]);

        // A missing stage is an answer, not a failure: an add/add conflict has no base, and a
        // delete/modify has only one side.
        GitRawResult result = await _runner.RunRawAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0 ? result.StandardOutput : null;
    }

    private async Task<bool> IsBinaryAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken)
    {
        foreach (int stage in new[] { OursStage, TheirsStage, BaseStage })
        {
            byte[]? content = await ReadStageAsync(repository, stage, path, cancellationToken).ConfigureAwait(false);

            if (content is not null)
            {
                return LooksBinary(content);
            }
        }

        return false;
    }
}
