using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// Reads a repository's submodules and puts them on the commits the repository expects.
/// </summary>
/// <remarks>
/// <para>
/// A submodule is a repository inside another one. The outer repository does not store its files: it
/// records one commit for the submodule's folder — the <em>expected</em> commit — and git checks that
/// commit out in the folder, on no branch, when it updates the submodule. Nothing moves the folder by
/// itself: after a pull that changes the expected commit, the folder stays where it was until it is
/// updated.
/// </para>
/// <para>
/// Every update here keeps one rule: it never moves a submodule with uncommitted changes, or one on a
/// commit no branch, tag or remote branch contains — that commit would be lost from sight. Such a
/// submodule is skipped and named, with the reason.
/// </para>
/// </remarks>
public interface ISubmoduleService
{
    /// <summary>
    /// Lists a repository's submodules, and theirs.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The submodules, each followed by the ones nested in it, in git's order; empty at once — no git
    /// runs — when the work tree has no <c>.gitmodules</c>.
    /// </returns>
    /// <exception cref="GitCommandException">
    /// git could not read the submodules — for instance a folder recorded as a submodule that
    /// <c>.gitmodules</c> does not declare.
    /// </exception>
    Task<IReadOnlyList<GitSubmodule>> ListAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads submodules and puts them on the commits the repository expects, and the ones nested in
    /// them on the commits they expect.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="paths">
    /// The submodules to update, by <see cref="GitSubmodule.Path"/> — nested ones included — or
    /// <see langword="null"/> for every submodule.
    /// </param>
    /// <param name="progress">Receives git's progress, submodule by submodule.</param>
    /// <param name="cancellationToken">Cancels the update; what is done stays done.</param>
    /// <returns>What was updated, skipped and why, and what git could not update.</returns>
    /// <remarks>
    /// <c>git submodule update --init --checkout</c>, one submodule at a time: <c>--checkout</c> overrides
    /// a <c>submodule.&lt;name&gt;.update</c> setting, so nothing is ever rebased or merged, and the
    /// submodule ends on the expected commit with no branch checked out. A submodule is not moved when
    /// it has uncommitted changes or is on a commit no branch contains (<see cref="SubmoduleSkip"/>);
    /// the ones nested in it are still looked at, against the commit it stays on.
    /// </remarks>
    Task<SubmoduleUpdateResult> UpdateAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string>? paths = null,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts on their expected commits the submodules an operation moved the repository's expectation
    /// for — and only those, plus the ones not downloaded yet.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="oldSha">
    /// The commit HEAD was on before the operation, or <see langword="null"/> when there was none — every
    /// submodule then counts as moved.
    /// </param>
    /// <param name="newSha">The commit HEAD is on after it.</param>
    /// <param name="progress">Receives git's progress, submodule by submodule.</param>
    /// <param name="cancellationToken">Cancels the update; what is done stays done.</param>
    /// <returns>What was updated, skipped and why, and what git could not update.</returns>
    /// <remarks>
    /// The submodules whose recorded commit differs between the two commits
    /// (<see cref="ChangedPinsAsync"/>), and the ones <c>.gitmodules</c> declares that are not
    /// downloaded, go through <see cref="UpdateAsync"/>'s rule. A submodule the operation did not touch —
    /// one the user moved on purpose — stays where it is. Inside a submodule that moves, the same rule
    /// applies to the ones nested in it, between the commit it leaves and the one it goes to. Nothing
    /// runs without a <c>.gitmodules</c>.
    /// </remarks>
    Task<SubmoduleUpdateResult> FollowAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the commit a repository's HEAD is on — what <see cref="FollowAsync"/> compares.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The commit's full name, or <see langword="null"/> before the first commit.</returns>
    Task<string?> ReadHeadAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the submodules whose expected commit differs between two commits of the repository.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="oldSha">
    /// The first commit, or <see langword="null"/> or empty for none — every submodule of
    /// <paramref name="newSha"/> then counts.
    /// </param>
    /// <param name="newSha">The second commit.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The paths whose recorded commit was added, removed or changed, in git's order; the repository's
    /// own submodules only — a nested one moves with the submodule it is in.
    /// </returns>
    Task<IReadOnlyList<string>> ChangedPinsAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells whether a branch, a tag or a remote branch of a submodule contains a commit.
    /// </summary>
    /// <param name="submodule">The submodule, downloaded.</param>
    /// <param name="sha">The commit, as a full object name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Whether one does; <see langword="false"/> means moving the submodule off the commit would leave
    /// it where nothing finds it again.
    /// </returns>
    Task<bool> IsOnAnyRefAsync(GitSubmodule submodule, string sha, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISubmoduleService"/>, driving the real git executable.
/// </summary>
/// <remarks>
/// The list is read one repository at a time — the repository, then each downloaded submodule — rather
/// than with <c>git submodule status --recursive</c>, so each level's names and addresses come from its
/// own <c>.gitmodules</c>, and so an update can apply its rule at every level before it goes deeper.
/// </remarks>
public sealed class SubmoduleService : ISubmoduleService
{
    /// <summary>The file a repository declares its submodules in.</summary>
    public const string GitModulesFileName = ".gitmodules";

    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IGitCredentialResolver _credentials;
    private readonly ILogger<SubmoduleService> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="credentials">Signs a download in with the repository's profile.</param>
    /// <param name="logger">Receives what was skipped and why.</param>
    public SubmoduleService(
        IGitProcessRunner runner,
        IGitCommandFactory commandFactory,
        IGitCredentialResolver credentials,
        ILogger<SubmoduleService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _commandFactory = commandFactory;
        _credentials = credentials;
        _logger = logger;
    }

    /// <summary>
    /// Tells whether a work tree declares submodules — the check that keeps everything here free for a
    /// repository without any.
    /// </summary>
    /// <param name="workTreePath">The work tree's folder.</param>
    /// <returns>Whether it holds a <c>.gitmodules</c> file.</returns>
    public static bool HasGitModules(string workTreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        return File.Exists(Path.Combine(workTreePath, GitModulesFileName));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitSubmodule>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        List<GitSubmodule> submodules = [];
        await AppendTreeAsync(submodules, repository.WorkTreePath, parent: null, cancellationToken).ConfigureAwait(false);

        return submodules;
    }

    /// <inheritdoc />
    public async Task<SubmoduleUpdateResult> UpdateAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string>? paths = null,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!HasGitModules(repository.WorkTreePath) || paths is { Count: 0 })
        {
            return SubmoduleUpdateResult.Nothing;
        }

        GitCredentials credentials = await _credentials.ForRepositoryAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        UpdateRun run = new(repository, credentials, progress);

        if (paths is null)
        {
            foreach (GitSubmodule submodule in await ListLevelAsync(repository.WorkTreePath, null, cancellationToken)
                .ConfigureAwait(false))
            {
                await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
            }

            return run.ToResult();
        }

        IReadOnlyList<GitSubmodule> listed = await ListAsync(repository, cancellationToken).ConfigureAwait(false);
        HashSet<string> requested = new(StringComparer.Ordinal);

        foreach (string path in paths)
        {
            requested.Add(SubmoduleOutputParser.NormalisePath(path ?? string.Empty));
        }

        foreach (string path in requested)
        {
            // Updating a submodule updates the ones nested in it, against the commit it moves to; the
            // state listed for a nested one before that move would be stale.
            if (HasRequestedAncestor(path, requested, listed))
            {
                continue;
            }

            GitSubmodule? submodule = Find(listed, path);

            if (submodule is null)
            {
                run.Skip(new SubmoduleSkip(path, SubmoduleSkipReason.NotASubmodule));
                continue;
            }

            await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
        }

        return run.ToResult();
    }

    /// <inheritdoc />
    public async Task<SubmoduleUpdateResult> FollowAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        EnsureRevision(newSha, nameof(newSha));

        if (!HasGitModules(repository.WorkTreePath))
        {
            return SubmoduleUpdateResult.Nothing;
        }

        IReadOnlyList<string> changed = await ChangedPinsAsync(repository, oldSha, newSha, cancellationToken)
            .ConfigureAwait(false);

        GitCredentials credentials = await _credentials.ForRepositoryAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        UpdateRun run = new(repository, credentials, progress, followOnly: true);

        await FollowLevelAsync(run, repository.WorkTreePath, parent: null, changed, cancellationToken).ConfigureAwait(false);

        return run.ToResult();
    }

    /// <inheritdoc />
    public Task<string?> ReadHeadAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return ReadHeadAsync(repository.WorkTreePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ChangedPinsAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return await ChangedPinsInAsync(repository.WorkTreePath, oldSha, newSha, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ChangedPinsInAsync(
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

            return ParseGitlinksInTree(tree.StandardOutput);
        }

        EnsureRevision(oldSha, nameof(oldSha));

        GitResult diff = await _runner.RunAsync(
                _commandFactory.Create(directory, "diff-tree", "-r", "--no-renames", "-z", oldSha, newSha),
                throwOnError: true,
                cancellationToken)
            .ConfigureAwait(false);

        return ParseChangedGitlinks(diff.StandardOutput);
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Reads the paths of the gitlinks <c>git diff-tree -r --no-renames -z</c> reports as changed.
    /// </summary>
    /// <param name="output">
    /// What git printed: for each change, <c>:&lt;old mode&gt; &lt;new mode&gt; &lt;old&gt; &lt;new&gt;
    /// &lt;status&gt;</c>, a NUL, the path and a NUL.
    /// </param>
    /// <returns>The paths where either side is a submodule (mode <c>160000</c>), in git's order.</returns>
    public static IReadOnlyList<string> ParseChangedGitlinks(string? output)
    {
        List<string> paths = [];

        if (string.IsNullOrEmpty(output))
        {
            return paths;
        }

        string[] fields = output.Split('\0');

        for (int index = 0; index + 1 < fields.Length; index++)
        {
            string header = fields[index];

            if (!header.StartsWith(':'))
            {
                continue;
            }

            string[] parts = header.Substring(1).Split(' ');
            string path = fields[++index];

            if (parts.Length >= 2 && (parts[0] == SubmoduleOutputParser.GitlinkMode || parts[1] == SubmoduleOutputParser.GitlinkMode))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Reads the paths of the gitlinks in <c>git ls-tree -r -z</c>'s output.
    /// </summary>
    /// <param name="output">What git printed: <c>&lt;mode&gt; &lt;type&gt; &lt;object&gt;</c>, a tab and the path, per NUL-ended record.</param>
    /// <returns>The submodules' paths, in git's order.</returns>
    public static IReadOnlyList<string> ParseGitlinksInTree(string? output)
    {
        List<string> paths = [];

        if (string.IsNullOrEmpty(output))
        {
            return paths;
        }

        foreach (string record in output.Split('\0'))
        {
            int tab = record.IndexOf('\t');

            if (tab > 0 && record.StartsWith(SubmoduleOutputParser.GitlinkMode + " ", StringComparison.Ordinal))
            {
                paths.Add(record.Substring(tab + 1));
            }
        }

        return paths;
    }

    /// <summary>
    /// Summarises what git wrote while failing to update a submodule: its errors, or else its last line
    /// — never the progress chatter before them.
    /// </summary>
    /// <param name="standardError">Everything git wrote to standard error.</param>
    /// <returns>The message, with any credential redacted.</returns>
    public static string SummariseFailure(string? standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return "git could not update it.";
        }

        List<string> errors = [];
        string last = string.Empty;

        foreach (string raw in standardError.Split('\n', '\r'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            last = line;

            if (line.StartsWith("fatal:", StringComparison.Ordinal) || line.StartsWith("error:", StringComparison.Ordinal))
            {
                errors.Add(line);
            }
        }

        return ArgumentRedactor.Redact(errors.Count > 0 ? string.Join(Environment.NewLine, errors) : last);
    }

    private static bool HasRequestedAncestor(string path, HashSet<string> requested, IReadOnlyList<GitSubmodule> listed)
    {
        for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
        {
            string ancestor = path.Substring(0, slash);

            if (requested.Contains(ancestor) && Find(listed, ancestor) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static GitSubmodule? Find(IReadOnlyList<GitSubmodule> submodules, string path)
    {
        foreach (GitSubmodule submodule in submodules)
        {
            if (string.Equals(submodule.Path, path, StringComparison.Ordinal))
            {
                return submodule;
            }
        }

        return null;
    }

    private static void EnsureRevision(string? revision, string parameterName)
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
    private async Task<List<GitSubmodule>> ListLevelAsync(
        string directory,
        GitSubmodule? parent,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? onlyThese = null)
    {
        List<GitSubmodule> submodules = [];

        if (!HasGitModules(directory))
        {
            return submodules;
        }

        GitResult modules = await _runner.RunAsync(
                _commandFactory.Create(directory, "config", "-f", GitModulesFileName, "-z", "--get-regexp", @"^submodule\."),
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

    private static bool IsCheckedOut(string fullPath)
    {
        string dotGit = Path.Combine(fullPath, ".git");

        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }

    private async Task<string?> ReadHeadAsync(string fullPath, CancellationToken cancellationToken)
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

    private async Task UpdateTreeAsync(UpdateRun run, GitSubmodule submodule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (submodule.State)
        {
            case SubmoduleCheckoutState.Conflicted:
                run.Skip(new SubmoduleSkip(submodule.Path, SubmoduleSkipReason.Conflicted, submodule.CheckedOutSha));
                return;

            case SubmoduleCheckoutState.UpToDate:
                break;

            case SubmoduleCheckoutState.NotDownloaded:
                if (!await CheckOutAsync(run, submodule, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                // Everything in a submodule just downloaded is still to download.
                await UpdateNestedAsync(run, submodule, cancellationToken).ConfigureAwait(false);
                return;

            default:
                if (submodule.HasUncommittedChanges)
                {
                    run.Skip(new SubmoduleSkip(submodule.Path, SubmoduleSkipReason.UncommittedChanges, submodule.CheckedOutSha));
                }
                else if (!await IsOnAnyRefAsync(submodule, submodule.CheckedOutSha!, cancellationToken).ConfigureAwait(false))
                {
                    run.Skip(new SubmoduleSkip(submodule.Path, SubmoduleSkipReason.CommitOnNoBranch, submodule.CheckedOutSha));
                }
                else if (!await CheckOutAsync(run, submodule, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                else if (run.FollowOnly)
                {
                    // Moved: the nested ones follow what this move changed for them.
                    IReadOnlyList<string> moved = await ChangedPinsInAsync(
                            submodule.FullPath,
                            submodule.CheckedOutSha,
                            submodule.ExpectedSha,
                            cancellationToken)
                        .ConfigureAwait(false);

                    await FollowLevelAsync(run, submodule.FullPath, submodule, moved, cancellationToken).ConfigureAwait(false);
                    return;
                }

                break;
        }

        if (run.FollowOnly)
        {
            // Not moved: nothing changed for the nested ones but those still to download.
            await FollowLevelAsync(run, submodule.FullPath, submodule, [], cancellationToken).ConfigureAwait(false);
            return;
        }

        await UpdateNestedAsync(run, submodule, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates every submodule nested in one, listed again: after a move, their expected commits are the
    /// new commit's.
    /// </summary>
    private async Task UpdateNestedAsync(UpdateRun run, GitSubmodule submodule, CancellationToken cancellationToken)
    {
        foreach (GitSubmodule nested in await ListLevelAsync(submodule.FullPath, submodule, cancellationToken)
            .ConfigureAwait(false))
        {
            await UpdateTreeAsync(run, nested, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Follows one repository level: its submodules whose recorded commit moved, and the ones not
    /// downloaded.
    /// </summary>
    private async Task FollowLevelAsync(
        UpdateRun run,
        string directory,
        GitSubmodule? parent,
        IReadOnlyCollection<string> moved,
        CancellationToken cancellationToken)
    {
        HashSet<string> paths = new(moved, StringComparer.Ordinal);

        foreach (GitSubmodule submodule in await ListLevelAsync(directory, parent, cancellationToken, paths)
            .ConfigureAwait(false))
        {
            string local = parent is null ? submodule.Path : submodule.Path[(parent.Path.Length + 1)..];

            if (paths.Contains(local) || submodule.State == SubmoduleCheckoutState.NotDownloaded)
            {
                await UpdateTreeAsync(run, submodule, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs <c>git submodule update</c> for one submodule, from the repository it belongs to.
    /// </summary>
    /// <returns>Whether git succeeded.</returns>
    private async Task<bool> CheckOutAsync(UpdateRun run, GitSubmodule submodule, CancellationToken cancellationToken)
    {
        string directory;
        string local;

        if (submodule.ParentPath is null)
        {
            directory = run.Repository.WorkTreePath;
            local = submodule.Path;
        }
        else
        {
            directory = Path.GetFullPath(Path.Combine(run.Repository.WorkTreePath, submodule.ParentPath));
            local = submodule.Path.Substring(submodule.ParentPath.Length + 1);
        }

        run.Progress?.Report(SubmoduleUpdateProgress.Starting(submodule.Path));

        GitCommand command = _commandFactory
            .Create(directory, "submodule", "update", "--init", "--checkout", "--progress", "--", local)
            .WithCredentials(run.Credentials);

        ProgressRelay? relay = run.Progress is null ? null : new ProgressRelay(submodule.Path, run.Progress);

        GitResult result = await _runner.RunStreamingAsync(command, relay, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            run.Updated.Add(submodule.Path);
            return true;
        }

        string message = SummariseFailure(result.StandardError);
        _logger.LogWarning("The submodule {Path} could not be updated: {Message}", submodule.Path, message);
        run.Failed.Add(new SubmoduleFailure(submodule.Path, message));

        return false;
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// What one update has done so far.
    /// </summary>
    private sealed class UpdateRun
    {
        public UpdateRun(
            RepositoryHandle repository,
            GitCredentials credentials,
            IProgress<SubmoduleUpdateProgress>? progress,
            bool followOnly = false)
        {
            Repository = repository;
            Credentials = credentials;
            Progress = progress;
            FollowOnly = followOnly;
        }

        /// <summary>
        /// Gets a value indicating whether nested submodules are updated only where their parent's move
        /// changed them (a follow), rather than all of them (an update).
        /// </summary>
        public bool FollowOnly { get; }

        public RepositoryHandle Repository { get; }

        public GitCredentials Credentials { get; }

        public IProgress<SubmoduleUpdateProgress>? Progress { get; }

        public List<string> Updated { get; } = [];

        public List<SubmoduleSkip> Skipped { get; } = [];

        public List<SubmoduleFailure> Failed { get; } = [];

        public void Skip(SubmoduleSkip skip) => Skipped.Add(skip);

        public SubmoduleUpdateResult ToResult() => new(Updated, Skipped, Failed);
    }

    /// <summary>
    /// Turns the chunks git writes into progress reports for one submodule.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="IProgress{T}"/>, not a <see cref="Progress{T}"/>, for the reason
    /// <see cref="SyncService"/> gives: marshalling belongs to the caller.
    /// </remarks>
    private sealed class ProgressRelay : IProgress<string>
    {
        private readonly string _path;
        private readonly IProgress<SubmoduleUpdateProgress> _target;

        public ProgressRelay(string path, IProgress<SubmoduleUpdateProgress> target)
        {
            _path = path;
            _target = target;
        }

        public void Report(string value)
        {
            if (SyncProgressParser.Parse(value) is { } parsed)
            {
                _target.Report(new SubmoduleUpdateProgress(_path, parsed));
            }
        }
    }
}
