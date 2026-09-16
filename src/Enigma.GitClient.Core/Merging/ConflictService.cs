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

    /// <summary>
    /// Builds a conflicted file's regions, ready to resolve.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The document, or <see langword="null"/> when the file cannot be merged line by line — a
    /// binary file, or one side that does not exist.
    /// </returns>
    Task<ConflictDocument?> GetDocumentAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the resolved file and stages it.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="text">Exactly what the file should contain.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is resolved and staged.</returns>
    Task ResolveAsync(
        RepositoryHandle repository,
        string path,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes one side of a conflict whole, for the files that cannot be merged line by line.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="side">Which side to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is resolved and staged.</returns>
    Task ResolveWithAsync(
        RepositoryHandle repository,
        string path,
        ConflictSide side,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a file the user has resolved by hand.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is staged.</returns>
    Task MarkResolvedAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the same side of every conflict left in the repository.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="side">Which side to keep.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>The paths that were resolved, in the order they were listed.</returns>
    Task<IReadOnlyList<string>> ResolveAllWithAsync(
        RepositoryHandle repository,
        ConflictSide side,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Which whole version of a conflicted file to keep.
/// </summary>
public enum ConflictSide
{
    /// <summary>Ours: the version the branch being merged into had.</summary>
    Ours,

    /// <summary>Theirs: the version being merged in.</summary>
    Theirs,
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

    /// <inheritdoc />
    public async Task<ConflictDocument?> GetDocumentAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ConflictSides sides = await GetSidesAsync(repository, path, cancellationToken).ConfigureAwait(false);

        if (sides.IsBinary || sides.Ours is null || sides.Theirs is null)
        {
            // Nothing to merge line by line: a binary file, or a side that no longer exists. Those
            // are whole-file choices, which the caller makes with ResolveWithAsync.
            return null;
        }

        string? merged = await RunMergeFileAsync(repository, sides, cancellationToken).ConfigureAwait(false);

        // git's own merge is used wherever it can be, so the regions match what it would have
        // written; the direct merge is the fallback for when it cannot.
        return merged is not null
            ? ConflictDocumentBuilder.Parse(merged, ConflictDocument.DetectLineEnding(sides.Ours))
            : ConflictDocumentBuilder.Merge(sides.Base, sides.Ours, sides.Theirs);
    }

    /// <inheritdoc />
    public async Task ResolveAsync(
        RepositoryHandle repository,
        string path,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        string full = System.IO.Path.Combine(
            repository.WorkTreePath,
            path.Replace('/', System.IO.Path.DirectorySeparatorChar));

        string? directory = System.IO.Path.GetDirectoryName(full);

        if (directory is { Length: > 0 })
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // Written exactly as given, with no byte-order mark and no line-ending translation: the
        // text came from a preview, and a preview that is not what gets written is not a preview.
        await System.IO.File
            .WriteAllTextAsync(full, text, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        await StageAsync(repository, path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResolveWithAsync(
        RepositoryHandle repository,
        string path,
        ConflictSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["checkout", side == ConflictSide.Ours ? "--ours" : "--theirs", "--", path]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // A side that does not exist cannot be checked out; keeping "their deletion" means
            // removing the file, which is the honest reading of the choice.
            await RunAsync(repository, ["rm", "--quiet", "--force", "--", path], cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        await StageAsync(repository, path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task MarkResolvedAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return StageAsync(repository, path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ResolveAllWithAsync(
        RepositoryHandle repository,
        ConflictSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        IReadOnlyList<ConflictFile> files = await GetConflictsAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        List<string> resolved = [];

        foreach (ConflictFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // One side taken whole is the same thing as choosing that side for every region, and it
            // is the only form that also works for the binary and whole-file conflicts in the list.
            await ResolveWithAsync(repository, file.Path, side, cancellationToken).ConfigureAwait(false);

            resolved.Add(file.Path);
        }

        return resolved;
    }

    /// <summary>
    /// Runs git's own three-way merge over the stages, through temporary files.
    /// </summary>
    /// <returns>The merged text, or <see langword="null"/> when git could not produce one.</returns>
    private async Task<string?> RunMergeFileAsync(
        RepositoryHandle repository,
        ConflictSides sides,
        CancellationToken cancellationToken)
    {
        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"enigma-merge-{Guid.NewGuid():N}");

        System.IO.Directory.CreateDirectory(root);

        try
        {
            UTF8Encoding encoding = new(false);

            string ours = System.IO.Path.Combine(root, "ours");
            string baseFile = System.IO.Path.Combine(root, "base");
            string theirs = System.IO.Path.Combine(root, "theirs");

            await System.IO.File.WriteAllTextAsync(ours, sides.Ours, encoding, cancellationToken).ConfigureAwait(false);
            await System.IO.File.WriteAllTextAsync(baseFile, sides.Base ?? string.Empty, encoding, cancellationToken).ConfigureAwait(false);
            await System.IO.File.WriteAllTextAsync(theirs, sides.Theirs, encoding, cancellationToken).ConfigureAwait(false);

            GitCommand command = _commandFactory.Create(
                repository.WorkTreePath,
                ["merge-file", "--diff3", "-p", ours, baseFile, theirs]);

            // A conflicting merge exits with the number of conflicts, which is an answer rather
            // than a failure; only a negative status means git could not do it at all.
            GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
                .ConfigureAwait(false);

            return result.ExitCode >= 0 && result.StandardOutput.Length > 0 ? result.StandardOutput : null;
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private Task StageAsync(RepositoryHandle repository, string path, CancellationToken cancellationToken)
        => RunAsync(repository, ["add", "--", path], cancellationToken);

    private async Task RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
            }
        }
        catch (System.IO.IOException)
        {
            // A temporary directory that outlives the process is not worth failing a merge over.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
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
