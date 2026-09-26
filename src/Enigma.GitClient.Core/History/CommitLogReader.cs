using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.History;

/// <summary>
/// Reads commits with <c>git log</c> and the client's separator-based format template.
/// </summary>
public sealed class CommitLogReader : ICommitLogReader
{
    /// <summary>
    /// The fragments of git's standard error that mean "this repository has no commits yet", as
    /// opposed to a genuine failure. An unborn HEAD is a normal state for a freshly created
    /// repository, so it must read as an empty page rather than as an error.
    /// </summary>
    private static readonly string[] EmptyHistoryMarkers =
    [
        "does not have any commits yet",
        "bad default revision",
        "unknown revision or path not in the working tree",
        "bad revision 'HEAD'",
        "ambiguous argument 'HEAD'",
    ];

    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the log command.</param>
    /// <param name="commandFactory">Builds the log command.</param>
    public CommitLogReader(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task<CommitLogPage> GetPageAsync(
        RepositoryHandle repository,
        CommitLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Take <= 0)
        {
            return new CommitLogPage([], query.Skip, HasMore: false);
        }

        // One extra commit is requested so "is there another page?" needs no second walk.
        List<string> arguments = BuildArguments(query, query.Take + 1);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);
        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            if (IsEmptyHistory(result.StandardError))
            {
                return CommitLogPage.Empty;
            }

            throw new GitCommandException(command, result.ExitCode, result.StandardError, result.StandardOutput);
        }

        IReadOnlyList<GitCommit> commits = CommitLogParser.Parse(result.StandardOutput);

        bool hasMore = commits.Count > query.Take;
        if (hasMore)
        {
            List<GitCommit> trimmed = new(query.Take);
            for (int index = 0; index < query.Take; index++)
            {
                trimmed.Add(commits[index]);
            }

            commits = trimmed;
        }

        return new CommitLogPage(commits, query.Skip, hasMore);
    }

    /// <inheritdoc />
    public async Task<GitCommit?> GetCommitAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            "log",
            "--no-show-signature",
            "--max-count=1",
            $"--pretty=format:{CommitLogParser.FormatTemplate}",
            revision,
            "--");

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return null;
        }

        IReadOnlyList<GitCommit> commits = CommitLogParser.Parse(result.StandardOutput);
        return commits.Count == 0 ? null : commits[0];
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        RepositoryHandle repository,
        CommitLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(query);

        List<string> arguments = BuildArguments(query with { Skip = 0 }, take: null, countOnly: true);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);
        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return IsEmptyHistory(result.StandardError)
                ? 0
                : throw new GitCommandException(command, result.ExitCode, result.StandardError, result.StandardOutput);
        }

        return int.TryParse(result.TrimmedOutput, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }

    /// <summary>
    /// Builds the <c>git log</c> — or, for a count, <c>git rev-list --count</c> — arguments for a query.
    /// Public so the argument vector can be unit-tested.
    /// </summary>
    /// <param name="query">What to read.</param>
    /// <param name="take">How many commits to ask for, or <see langword="null"/> for no limit.</param>
    /// <param name="countOnly">Whether the arguments are for counting rather than reading.</param>
    /// <returns>The arguments, without the executable.</returns>
    public static List<string> BuildArguments(CommitLogQuery query, int? take, bool countOnly = false)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Counting is rev-list's job: it walks the same selectors and filters but prints only a
        // number, so a count never pays for formatting a payload that is thrown away.
        List<string> arguments = countOnly ? ["rev-list", "--count"] : ["log"];

        if (!countOnly)
        {
            // A user configuring log.showSignature would otherwise inject PGP output into every
            // record and break the parser.
            arguments.Add("--no-show-signature");
            arguments.Add($"--pretty=format:{CommitLogParser.FormatTemplate}");
        }

        switch (query.Ordering)
        {
            case CommitLogOrdering.Date:
                arguments.Add("--date-order");
                break;
            case CommitLogOrdering.Topological:
                arguments.Add("--topo-order");
                break;
            case CommitLogOrdering.Default:
            default:
                break;
        }

        if (query.FirstParentOnly)
        {
            arguments.Add("--first-parent");
        }

        if (query.Skip > 0)
        {
            arguments.Add($"--skip={query.Skip.ToString(CultureInfo.InvariantCulture)}");
        }

        if (take.HasValue)
        {
            arguments.Add($"--max-count={take.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(query.AuthorFilter) || !string.IsNullOrWhiteSpace(query.MessageFilter))
        {
            // Fixed strings and case-insensitivity make a search box behave the way people expect,
            // instead of treating a stray '.' or '*' as a regular expression.
            arguments.Add("--fixed-strings");
            arguments.Add("--regexp-ignore-case");
        }

        if (!string.IsNullOrWhiteSpace(query.AuthorFilter))
        {
            arguments.Add($"--author={query.AuthorFilter}");
        }

        if (!string.IsNullOrWhiteSpace(query.MessageFilter))
        {
            arguments.Add($"--grep={query.MessageFilter}");
        }

        if (query.Since.HasValue)
        {
            arguments.Add($"--since={query.Since.Value.ToString("O", CultureInfo.InvariantCulture)}");
        }

        if (query.Until.HasValue)
        {
            arguments.Add($"--until={query.Until.Value.ToString("O", CultureInfo.InvariantCulture)}");
        }

        switch (query.Scope)
        {
            case CommitLogScope.AllRefs:
                // Before --all, and it has to be: an --exclude applies to the next --all, --branches,
                // --remotes or --glob on the command line, and to nothing after it.
                foreach (string reference in query.ExcludedRefs)
                {
                    if (IsExcludable(reference))
                    {
                        arguments.Add($"--exclude={reference}");
                    }
                }

                arguments.Add("--all");
                break;
            case CommitLogScope.Revision when !string.IsNullOrWhiteSpace(query.Revision):
                arguments.Add(query.Revision);
                break;
            case CommitLogScope.Head:
            case CommitLogScope.Revision:
            default:
                arguments.Add("HEAD");
                break;
        }

        // The "--" terminator keeps a ref name that also names a file from being ambiguous.
        arguments.Add("--");
        arguments.AddRange(query.PathFilters);

        return arguments;
    }

    /// <summary>
    /// Answers whether an entry of <see cref="CommitLogQuery.ExcludedRefs"/> can be handed to git as it
    /// is: a full ref name, which as a pattern only ever matches itself.
    /// </summary>
    private static bool IsExcludable(string? reference)
        => reference is { Length: > 5 }
           && reference.StartsWith("refs/", StringComparison.Ordinal)
           && reference.AsSpan().IndexOfAny("*?[ \t\n") < 0;

    private static bool IsEmptyHistory(string standardError)
    {
        foreach (string marker in EmptyHistoryMarkers)
        {
            if (standardError.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
