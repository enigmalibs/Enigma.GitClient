using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Status;

/// <summary>
/// Reads what the working tree and the index look like.
/// </summary>
public interface IStatusService
{
    /// <summary>
    /// Reads the repository's status.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="includeIgnored">Whether to list ignored files as well.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status.</returns>
    Task<WorkingTreeStatus> GetStatusAsync(
        RepositoryHandle repository,
        bool includeIgnored = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IStatusService"/>.
/// </summary>
public sealed class StatusService : IStatusService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the command.</param>
    /// <param name="commandFactory">Builds the command.</param>
    public StatusService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <summary>
    /// Builds the argument vector the status read uses.
    /// </summary>
    /// <param name="includeIgnored">Whether to list ignored files.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <c>--untracked-files=all</c> lists files inside an untracked directory rather than the
    /// directory alone, which is what a user expects to be able to stage individually.
    /// <c>--ignore-submodules=none</c> is what makes a submodule's own dirt visible; git's default
    /// hides it, and a submodule that quietly differs is a classic way to commit the wrong thing.
    /// </remarks>
    public static List<string> BuildArguments(bool includeIgnored)
    {
        List<string> arguments =
        [
            "status",
            "--porcelain=v2",
            "-z",
            "--branch",
            "--untracked-files=all",
            "--ignore-submodules=none",
        ];

        if (includeIgnored)
        {
            arguments.Add("--ignored=matching");
        }

        return arguments;
    }

    /// <inheritdoc />
    public async Task<WorkingTreeStatus> GetStatusAsync(
        RepositoryHandle repository,
        bool includeIgnored = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, BuildArguments(includeIgnored));
        GitResult result = await _runner.RunAsync(command, throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        return PorcelainV2Parser.Parse(result.StandardOutput);
    }
}

/// <summary>
/// Answers whether a path is covered by an ignore rule, and which rule.
/// </summary>
public interface IGitIgnoreService
{
    /// <summary>
    /// Checks whether a path is ignored.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="path">The path, relative to the work tree.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The rule that ignores the path — the source file, the line and the pattern — or
    /// <see langword="null"/> when nothing ignores it.
    /// </returns>
    Task<IgnoreRule?> GetIgnoreRuleAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a pattern to the repository's root <c>.gitignore</c>.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="pattern">The pattern to append.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file has the pattern.</returns>
    Task AddPatternAsync(
        RepositoryHandle repository,
        string pattern,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The ignore rule that covers a path.
/// </summary>
/// <param name="Source">The file the rule is written in.</param>
/// <param name="Line">Its line number in that file.</param>
/// <param name="Pattern">The pattern itself.</param>
public sealed record IgnoreRule(string Source, int Line, string Pattern)
{
    /// <inheritdoc />
    public override string ToString()
        => $"{Pattern} ({Source}:{Line.ToString(CultureInfo.InvariantCulture)})";
}

/// <summary>
/// Default <see cref="IGitIgnoreService"/>.
/// </summary>
public sealed class GitIgnoreService : IGitIgnoreService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    public GitIgnoreService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <summary>
    /// Matches one <c>check-ignore --verbose</c> line: <c>source:line:pattern</c>, a tab, then the
    /// path that was asked about.
    /// </summary>
    /// <remarks>
    /// The source is matched greedily on purpose. A Windows source path carries a colon of its own,
    /// so anchoring on the <em>last</em> colon-digits-colon is what finds the line number rather
    /// than the drive letter.
    /// </remarks>
    private static readonly Regex VerboseLine = new(
        @"^(?<source>.*):(?<line>\d+):(?<pattern>.*)\t",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses <c>check-ignore --verbose</c> output.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <returns>The rule, or <see langword="null"/> when nothing matched.</returns>
    /// <remarks>
    /// <c>-z</c> is deliberately not used: git refuses it outside <c>--stdin</c>. Only the fields
    /// before the tab are read, and those are never quoted — the trailing path, which can be, is
    /// the one field this does not need.
    /// </remarks>
    public static IgnoreRule? ParseRule(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        foreach (string line in payload.Split('\n'))
        {
            Match match = VerboseLine.Match(line);

            if (!match.Success)
            {
                continue;
            }

            _ = int.TryParse(
                match.Groups["line"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int number);

            return new IgnoreRule(match.Groups["source"].Value, number, match.Groups["pattern"].Value);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IgnoreRule?> GetIgnoreRuleAsync(
        RepositoryHandle repository,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["check-ignore", "--verbose", "--non-matching", "--", path]);

        // Exit code 1 simply means "not ignored", which is an answer rather than a failure.
        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode is 0 or 1 ? ParseRule(result.StandardOutput) : null;
    }

    /// <inheritdoc />
    public async Task AddPatternAsync(
        RepositoryHandle repository,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        string file = System.IO.Path.Combine(repository.WorkTreePath, ".gitignore");
        string existing = System.IO.File.Exists(file)
            ? await System.IO.File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        foreach (string line in existing.Split('\n'))
        {
            if (string.Equals(line.Trim(), pattern.Trim(), StringComparison.Ordinal))
            {
                // Already there. Appending it again would be noise in a file people read.
                return;
            }
        }

        string separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";

        await System.IO.File
            .AppendAllTextAsync(file, $"{separator}{pattern.Trim()}\n", cancellationToken)
            .ConfigureAwait(false);
    }
}
