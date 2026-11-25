using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// The reading half of <see cref="SubmoduleService"/>: a repository's submodules, their checkouts, the
/// pins a commit or the index changes, and whether a commit is on a ref.
/// </summary>
/// <remarks>
/// The list is read one repository at a time, the repository and then each downloaded submodule, so
/// each level's names and addresses come from its own <c>.gitmodules</c>.
/// </remarks>
internal sealed class SubmoduleReader
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly ILogger<SubmoduleService> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="logger">Receives what was ignored and why, under the service's category.</param>
    public SubmoduleReader(IGitProcessRunner runner, IGitCommandFactory commandFactory, ILogger<SubmoduleService> logger)
    {
        _runner = runner;
        _commandFactory = commandFactory;
        _logger = logger;
    }

    /// <inheritdoc cref="ISubmoduleService.ListAsync"/>
    public async Task<IReadOnlyList<GitSubmodule>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        List<GitSubmodule> submodules = [];
        await AppendTreeAsync(submodules, repository.WorkTreePath, parent: null, cancellationToken).ConfigureAwait(false);

        return submodules;
    }

    /// <inheritdoc cref="ISubmoduleService.ReadHeadAsync"/>
    public Task<string?> ReadHeadAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return ReadHeadAsync(repository.WorkTreePath, cancellationToken);
    }

    /// <inheritdoc cref="ISubmoduleService.ReadStagedPinsAsync"/>
    public async Task<IReadOnlyList<StagedPin>> ReadStagedPinsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!SubmoduleService.HasGitModules(repository.WorkTreePath))
        {
            return [];
        }

        GitResult diff = await _runner.RunAsync(
                _commandFactory.Create(repository.WorkTreePath, "diff", "--cached", "--raw", "--no-abbrev", "-z", "--no-renames", "--ignore-submodules=none"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        List<StagedPin> pins = [];

        if (!diff.IsSuccess)
        {
            return pins;
        }

        foreach ((string path, string oldSha, string newSha) in SubmoduleOutputParser.ParseChangedGitlinkEntries(diff.StandardOutput))
        {
            pins.Add(new StagedPin(path, oldSha, newSha));
        }

        return pins;
    }

    /// <inheritdoc cref="ISubmoduleService.ReadSuperprojectAsync"/>
    public async Task<string?> ReadSuperprojectAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(repository.WorkTreePath, "rev-parse", "--show-superproject-working-tree"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        string printed = result.TrimmedOutput.Trim();

        return result.IsSuccess && printed.Length > 0 && Path.IsPathFullyQualified(printed)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(printed))
            : null;
    }

    /// <inheritdoc cref="ISubmoduleService.ChangedPinsAsync"/>
    public async Task<IReadOnlyList<string>> ChangedPinsAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return await ChangedPinsInAsync(repository.WorkTreePath, oldSha, newSha, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<string>> ChangedPinsInAsync(
        string directory,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken)
    {
        EnsureRevision(newSha, nameof(newSha));

        if (string.IsNullOrEmpty(oldSha))
        {
            GitResult tree = await _runner.RunAsync(
                    _commandFactory.Create(directory, "ls-tree", "-r", "-z", newSha),
                    throwOnError: true,
                    cancellationToken)
                .ConfigureAwait(false);

            return SubmoduleOutputParser.ParseGitlinksInTree(tree.StandardOutput);
        }

        EnsureRevision(oldSha, nameof(oldSha));

        GitResult diff = await _runner.RunAsync(
                _commandFactory.Create(directory, "diff-tree", "-r", "--no-renames", "-z", oldSha, newSha),
                throwOnError: true,
                cancellationToken)
            .ConfigureAwait(false);

        return SubmoduleOutputParser.ParseChangedGitlinks(diff.StandardOutput);
    }

    /// <inheritdoc cref="ISubmoduleService.IsOnAnyRefAsync"/>
    public async Task<bool> IsOnAnyRefAsync(GitSubmodule submodule, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submodule);

        if (!SubmoduleOutputParser.IsObjectName(sha))
        {
            throw new ArgumentException("A commit is named by its full object name here.", nameof(sha));
        }

        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(
                    submodule.FullPath,
                    "for-each-ref",
                    "--count=1",
                    "--format=%(refname)",
                    "--contains",
                    sha,
                    "refs/heads",
                    "refs/tags",
                    "refs/remotes"),
                throwOnError: true,
                cancellationToken)
            .ConfigureAwait(false);

        return result.TrimmedOutput.Length > 0;
    }

    internal async Task<int> CountCommitsAsync(string directory, IReadOnlyList<string> revisions, CancellationToken cancellationToken)
    {
        List<string> arguments = ["rev-list", "--count", .. revisions];

        GitResult result = await _runner.RunAsync(_commandFactory.Create(directory, arguments), throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess && int.TryParse(result.TrimmedOutput, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }

    internal static void EnsureRevision(string? revision, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision, parameterName);

        if (revision.StartsWith('-'))
        {
            throw new ArgumentException("A revision cannot start with a dash.", parameterName);
        }
    }

    private async Task AppendTreeAsync(
        List<GitSubmodule> into,
        string directory,
        GitSubmodule? parent,
        CancellationToken cancellationToken)
    {
        foreach (GitSubmodule submodule in await ListLevelAsync(directory, parent, cancellationToken).ConfigureAwait(false))
        {
            into.Add(submodule);

            if (submodule.IsDownloaded)
            {
                await AppendTreeAsync(into, submodule.FullPath, submodule, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Lists the submodules of one repository — the opened one, or a submodule — without going deeper.
    /// </summary>
    /// <remarks>
    /// The folders <c>.gitmodules</c> declares, the commits the index records for them
    /// (<c>git ls-files --stage</c>), and each downloaded one's HEAD. Not <c>git submodule status</c>:
    /// it runs <c>git describe</c> up to four times per submodule, some 1.6 seconds for four submodules
    /// on Windows, for a description nothing here shows. A folder the index records as a submodule but
    /// <c>.gitmodules</c> does not declare is not listed — git cannot update it either.
    /// </remarks>
    internal async Task<List<GitSubmodule>> ListLevelAsync(
        string directory,
        GitSubmodule? parent,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? onlyThese = null)
    {
        List<GitSubmodule> submodules = [];

        if (!SubmoduleService.HasGitModules(directory))
        {
            return submodules;
        }

        GitResult modules = await _runner.RunAsync(
                _commandFactory.Create(directory, "config", "-f", SubmoduleService.GitModulesFileName, "-z", "--get-regexp", @"^submodule\."),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, GitModulesEntry> declared = new(StringComparer.Ordinal);

        foreach (GitModulesEntry entry in SubmoduleOutputParser.ParseGitModules(modules.IsSuccess ? modules.StandardOutput : null))
        {
            declared.TryAdd(entry.Path, entry);
        }

        if (declared.Count == 0)
        {
            return submodules;
        }

        // --literal-pathspecs: a declared path is a folder name, never a pattern.
        List<string> arguments = ["--literal-pathspecs", "ls-files", "--stage", "-z", "--"];
        arguments.AddRange(declared.Keys);

        GitResult index = await _runner.RunAsync(_commandFactory.Create(directory, arguments), throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        // In index order; a conflicted submodule has up to three entries and no resolved one.
        List<string> order = [];
        Dictionary<string, (string? Expected, bool Conflicted)> recorded = new(StringComparer.Ordinal);

        foreach (GitlinkEntry entry in SubmoduleOutputParser.ParseGitlinks(index.StandardOutput))
        {
            if (!recorded.TryGetValue(entry.Path, out (string? Expected, bool Conflicted) found))
            {
                order.Add(entry.Path);
                found = (null, false);
            }

            recorded[entry.Path] = entry.Stage == 0 ? (entry.Sha, found.Conflicted) : (found.Expected, true);
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

        foreach (string local in order)
        {
            string fullPath = Path.GetFullPath(Path.Combine(root, local));

            // git refuses paths that climb out of the work tree; a folder that would anyway is not
            // looked into.
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
            {
                _logger.LogWarning("The submodule path {Path} in {Directory} leads outside it and is ignored", local, root);
                continue;
            }

            // A follow describes the moved ones and the empty ones only; telling a folder is empty
            // takes no git process.
            if (onlyThese is not null && !onlyThese.Contains(local) && IsCheckedOut(fullPath))
            {
                continue;
            }

            (string? expected, bool conflicted) = recorded[local];

            GitSubmodule submodule = await DescribeAsync(
                    declared[local],
                    local,
                    fullPath,
                    conflicted ? string.Empty : expected ?? string.Empty,
                    conflicted,
                    parent,
                    cancellationToken)
                .ConfigureAwait(false);

            submodules.Add(submodule);
        }

        return submodules;
    }

    private async Task<GitSubmodule> DescribeAsync(
        GitModulesEntry entry,
        string local,
        string fullPath,
        string expected,
        bool conflicted,
        GitSubmodule? parent,
        CancellationToken cancellationToken)
    {
        string? checkedOut = await ReadHeadAsync(fullPath, cancellationToken).ConfigureAwait(false);

        SubmoduleCheckoutState state;

        if (conflicted)
        {
            state = SubmoduleCheckoutState.Conflicted;
        }
        else if (checkedOut is null)
        {
            state = SubmoduleCheckoutState.NotDownloaded;
        }
        else if (string.Equals(checkedOut, expected, StringComparison.OrdinalIgnoreCase))
        {
            state = SubmoduleCheckoutState.UpToDate;
        }
        else
        {
            state = await CompareAsync(fullPath, checkedOut, expected, cancellationToken).ConfigureAwait(false);
        }

        (bool modified, bool untracked) = checkedOut is null
            ? (false, false)
            : await ReadDirtAsync(fullPath, cancellationToken).ConfigureAwait(false);

        return new GitSubmodule
        {
            Name = entry.Name,
            Path = parent is null ? local : parent.Path + "/" + local,
            FullPath = fullPath,
            Url = ArgumentRedactor.Redact(entry.Url),
            ExpectedSha = expected,
            CheckedOutSha = checkedOut,
            State = state,
            HasUncommittedChanges = modified,
            HasUntrackedFiles = untracked,
            ParentPath = parent?.Path,
        };
    }

    /// <summary>
    /// Works out where a submodule that is not on its expected commit stands.
    /// </summary>
    /// <remarks>
    /// An expected commit the submodule does not have yet — a colleague's, not fetched — counts as
    /// <see cref="SubmoduleCheckoutState.Behind"/>: it can only come from the remote, so the checkout
    /// is older than what the repository expects, and an update fetches it.
    /// </remarks>
    private async Task<SubmoduleCheckoutState> CompareAsync(
        string fullPath,
        string checkedOut,
        string expected,
        CancellationToken cancellationToken)
    {
        if (!SubmoduleOutputParser.IsObjectName(expected))
        {
            return SubmoduleCheckoutState.Elsewhere;
        }

        int behind = await IsAncestorAsync(fullPath, checkedOut, expected, cancellationToken).ConfigureAwait(false);

        if (behind != 1)
        {
            // 0: an ancestor; anything else: git does not know the expected commit.
            return SubmoduleCheckoutState.Behind;
        }

        int ahead = await IsAncestorAsync(fullPath, expected, checkedOut, cancellationToken).ConfigureAwait(false);

        return ahead == 0 ? SubmoduleCheckoutState.NewCommits : SubmoduleCheckoutState.Elsewhere;
    }

    private async Task<int> IsAncestorAsync(string directory, string ancestor, string descendant, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(directory, "merge-base", "--is-ancestor", ancestor, descendant),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode;
    }

    /// <summary>
    /// Reads a submodule's own uncommitted changes, leaving out the submodules nested in it: one of
    /// those that is merely on another commit is not work that an update could lose.
    /// </summary>
    private async Task<(bool Modified, bool Untracked)> ReadDirtAsync(string fullPath, CancellationToken cancellationToken)
    {
        GitResult status = await _runner.RunAsync(
                _commandFactory.Create(fullPath, "status", "--porcelain=v2", "--ignore-submodules=all"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        bool modified = false;
        bool untracked = false;

        foreach (string line in status.SplitOutput())
        {
            if (line.Length < 2 || line[1] != ' ')
            {
                continue;
            }

            switch (line[0])
            {
                case '1' or '2' or 'u':
                    modified = true;
                    break;

                case '?':
                    untracked = true;
                    break;
            }
        }

        return (modified, untracked);
    }

    internal static bool IsCheckedOut(string fullPath)
    {
        string dotGit = Path.Combine(fullPath, ".git");

        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }

    internal async Task<string?> ReadHeadAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!IsCheckedOut(fullPath))
        {
            return null;
        }

        GitResult head = await _runner.RunAsync(
                _commandFactory.Create(fullPath, "rev-parse", "--verify", "--quiet", "HEAD"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        return head.IsSuccess && SubmoduleOutputParser.IsObjectName(head.TrimmedOutput) ? head.TrimmedOutput : null;
    }

    internal static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
