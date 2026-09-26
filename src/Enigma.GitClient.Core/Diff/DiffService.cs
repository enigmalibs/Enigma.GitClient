using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// How a diff is produced.
/// </summary>
public sealed record DiffOptions
{
    /// <summary>
    /// The options the viewer uses.
    /// </summary>
    public static readonly DiffOptions Default = new();

    /// <summary>
    /// Gets how many unchanged lines are shown around each change.
    /// </summary>
    public int ContextLines { get; init; } = 3;

    /// <summary>
    /// Gets a value indicating whether whitespace-only changes are ignored.
    /// </summary>
    public bool IgnoreAllWhitespace { get; init; }

    /// <summary>
    /// Gets a value indicating whether changes that only add or remove blank lines are ignored.
    /// </summary>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>
    /// Gets a value indicating whether renames and copies are detected.
    /// </summary>
    public bool DetectRenames { get; init; } = true;

    /// <summary>
    /// Gets the parse limits applied to the resulting patch.
    /// </summary>
    public DiffParseOptions Parsing { get; init; } = DiffParseOptions.Default;
}

/// <summary>
/// Reads what a change touched, and the patch behind it.
/// </summary>
public interface IDiffService
{
    /// <summary>
    /// Lists the files a comparison touches, with their per-file line counts.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="target">What to compare.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The changed files, in the order git listed them.</returns>
    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
        RepositoryHandle repository,
        DiffTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the patch for one file of a comparison.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="target">What to compare.</param>
    /// <param name="path">The file's path, as the changed-files listing reported it.</param>
    /// <param name="options">How the diff is produced.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The parsed patch, or <see langword="null"/> when the file has no textual diff.</returns>
    Task<FilePatch?> GetPatchAsync(
        RepositoryHandle repository,
        DiffTarget target,
        string path,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the patch for one file of a comparison, given the entry the changed-files listing
    /// produced.
    /// </summary>
    /// <remarks>
    /// Prefer this overload wherever the listing is at hand. git detects a rename by pairing a
    /// deletion with an addition, and it does that <em>after</em> the pathspec has filtered the
    /// diff — so restricting the diff to the new path alone hides the old one and the rename is
    /// reported as a plain addition. Passing the entry lets both sides through.
    /// </remarks>
    /// <param name="repository">The repository to read.</param>
    /// <param name="target">What to compare.</param>
    /// <param name="file">The changed file to read the patch of.</param>
    /// <param name="options">How the diff is produced.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The parsed patch, or <see langword="null"/> when the file has no textual diff.</returns>
    Task<FilePatch?> GetPatchAsync(
        RepositoryHandle repository,
        DiffTarget target,
        ChangedFile file,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IDiffService"/>, built on git's own diff.
/// </summary>
public sealed class DiffService : IDiffService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the diff commands.</param>
    /// <param name="commandFactory">Builds the diff commands.</param>
    public DiffService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
        RepositoryHandle repository,
        DiffTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(target);

        GitResult nameStatus = await RunAsync(
                repository,
                BuildArguments(target, ["--name-status"], DiffOptions.Default, paths: null),
                cancellationToken)
            .ConfigureAwait(false);

        GitResult numstat = await RunAsync(
                repository,
                BuildArguments(target, ["--numstat"], DiffOptions.Default, paths: null),
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ChangedFile> files = NameStatusParser.ParseNameStatus(
            nameStatus.StandardOutput,
            StagingFor(target));

        return NameStatusParser.Combine(files, NameStatusParser.ParseNumstat(numstat.StandardOutput));
    }

    /// <inheritdoc />
    public Task<FilePatch?> GetPatchAsync(
        RepositoryHandle repository,
        DiffTarget target,
        string path,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return GetPatchCoreAsync(repository, target, [path], options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FilePatch?> GetPatchAsync(
        RepositoryHandle repository,
        DiffTarget target,
        ChangedFile file,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        List<string> paths = [file.Path];

        // The rename's other side, so git can still pair the two halves inside the pathspec.
        if (file.OldPath is { Length: > 0 } oldPath && !string.Equals(oldPath, file.Path, StringComparison.Ordinal))
        {
            paths.Add(oldPath);
        }

        return GetPatchCoreAsync(repository, target, paths, options, cancellationToken);
    }

    private async Task<FilePatch?> GetPatchCoreAsync(
        RepositoryHandle repository,
        DiffTarget target,
        IReadOnlyList<string> paths,
        DiffOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(target);

        DiffOptions effective = options ?? DiffOptions.Default;

        GitResult result = await RunAsync(
                repository,
                BuildArguments(target, ["--patch"], effective, paths),
                cancellationToken)
            .ConfigureAwait(false);

        PatchSet patch = UnifiedDiffParser.Parse(result.StandardOutput, effective.Parsing);

        if (patch.Files.Count == 0)
        {
            return null;
        }

        // A rename shows up under its new path; asking for either side must find it.
        foreach (FilePatch file in patch.Files)
        {
            foreach (string path in paths)
            {
                if (string.Equals(file.NewPath, path, StringComparison.Ordinal) ||
                    string.Equals(file.OldPath, path, StringComparison.Ordinal))
                {
                    return file;
                }
            }
        }

        return patch.Files[0];
    }

    private async Task<GitResult> RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        return await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the argument vector for a comparison.
    /// </summary>
    /// <param name="target">What to compare.</param>
    /// <param name="format">The output-shaping arguments, such as <c>--name-status</c>.</param>
    /// <param name="options">How the diff is produced.</param>
    /// <param name="paths">The paths to restrict the diff to, or <see langword="null"/> for all of them.</param>
    /// <returns>The arguments.</returns>
    public static List<string> BuildArguments(
        DiffTarget target,
        IReadOnlyList<string> format,
        DiffOptions options,
        IReadOnlyList<string>? paths)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(options);

        List<string> arguments = [];

        // "git show" is what handles a root commit without a special case: it diffs against the
        // empty tree, so the first commit in a repository lists its files as added rather than
        // failing on a parent that does not exist.
        bool useShow = target.Kind == DiffTargetKind.Commit && target.ParentIndex == 0;

        if (useShow)
        {
            arguments.Add("show");
            arguments.Add("--format=");

            // For a merge, compare against the first parent: that is the diff a reviewer expects,
            // and it is the only one that reads like an ordinary patch.
            arguments.Add("-m");
            arguments.Add("--first-parent");
        }
        else
        {
            arguments.Add("diff");
        }

        arguments.AddRange(format);

        bool isPatch = Contains(format, "--patch");

        if (!isPatch)
        {
            // -z shapes the machine-readable listings (--name-status, --numstat, --raw); it has no
            // meaning for a patch, so it is not passed there.
            arguments.Add("-z");
        }

        if (options.DetectRenames)
        {
            arguments.Add("--find-renames");
            arguments.Add("--find-copies");
        }

        if (options.IgnoreAllWhitespace)
        {
            arguments.Add("--ignore-all-space");
        }

        if (options.IgnoreBlankLines)
        {
            arguments.Add("--ignore-blank-lines");
        }

        if (isPatch)
        {
            arguments.Add($"--unified={options.ContextLines.ToString(CultureInfo.InvariantCulture)}");
        }

        switch (target.Kind)
        {
            case DiffTargetKind.Commit when useShow:
                arguments.Add(target.From!);
                break;

            case DiffTargetKind.Commit:
                arguments.Add($"{target.From}^{(target.ParentIndex + 1).ToString(CultureInfo.InvariantCulture)}");
                arguments.Add(target.From!);
                break;

            case DiffTargetKind.Range:
                arguments.Add(target.From!);
                arguments.Add(target.To!);
                break;

            case DiffTargetKind.Staged:
                arguments.Add("--cached");
                break;

            case DiffTargetKind.Uncommitted:
                arguments.Add("HEAD");
                break;

            case DiffTargetKind.WorkingTree:
            default:
                break;
        }

        arguments.Add("--");

        if (paths is not null)
        {
            foreach (string path in paths)
            {
                if (path is { Length: > 0 })
                {
                    arguments.Add(path);
                }
            }
        }

        return arguments;
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (string candidate in values)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static FileStagingState StagingFor(DiffTarget target)
        => target.Kind switch
        {
            DiffTargetKind.Staged => FileStagingState.Staged,
            DiffTargetKind.WorkingTree => FileStagingState.Unstaged,
            _ => FileStagingState.NotApplicable,
        };
}
