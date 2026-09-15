using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// Reads references with a single <c>for-each-ref</c> call, and HEAD with two cheap
/// <c>rev-parse</c>-class calls.
/// </summary>
public sealed class RefReader : IRefReader
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the read commands.</param>
    /// <param name="commandFactory">Builds the read commands.</param>
    public RefReader(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task<RefCollection> GetRefsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            "for-each-ref",
            $"--format={RefParser.FormatTemplate}",
            "refs/");

        GitResult result = await _runner.RunAsync(command, throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        List<GitBranch> local = [];
        List<GitBranch> remote = [];
        List<GitTag> tags = [];
        List<GitRef> other = [];

        foreach (GitRef reference in RefParser.Parse(result.StandardOutput))
        {
            switch (reference)
            {
                case GitBranch { IsRemote: false } branch:
                    local.Add(branch);
                    break;
                case GitBranch branch:
                    // git keeps a symbolic refs/remotes/<remote>/HEAD that points at the remote's
                    // default branch. It is not a branch a user can check out, so it is dropped
                    // rather than shown twice in the branches list.
                    if (!branch.ShortName.EndsWith("/HEAD", StringComparison.Ordinal))
                    {
                        remote.Add(branch);
                    }

                    break;
                case GitTag tag:
                    tags.Add(tag);
                    break;
                default:
                    other.Add(reference);
                    break;
            }
        }

        local.Sort(CompareBranches);
        remote.Sort(CompareBranches);
        tags.Sort(static (left, right) =>
            string.Compare(left.ShortName, right.ShortName, StringComparison.OrdinalIgnoreCase));

        return new RefCollection(local, remote, tags, other);
    }

    /// <inheritdoc />
    public async Task<HeadState> GetHeadStateAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitResult symbolic = await _runner.RunAsync(
                _commandFactory.Create(repository.WorkTreePath, "symbolic-ref", "--quiet", "--short", "HEAD"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        GitResult resolved = await _runner.RunAsync(
                _commandFactory.Create(repository.WorkTreePath, "rev-parse", "--verify", "--quiet", "HEAD"),
                throwOnError: false,
                cancellationToken)
            .ConfigureAwait(false);

        RepositoryOperation operation = DetectOperation(repository);

        string sha = resolved.IsSuccess ? resolved.TrimmedOutput : string.Empty;
        bool isDetached = !symbolic.IsSuccess;
        string? branchName = symbolic.IsSuccess ? symbolic.TrimmedOutput : null;

        if (sha.Length == 0)
        {
            // HEAD names a branch that has no commits yet. git still reports the branch name, and
            // showing it matters: it is the branch the first commit will create.
            return HeadState.Unborn(branchName) with { Operation = operation };
        }

        return new HeadState(IsUnborn: false, isDetached, branchName, sha, operation);
    }

    /// <inheritdoc />
    public async Task<RepositoryRefState> GetStateAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        RefCollection refs = await GetRefsAsync(repository, cancellationToken).ConfigureAwait(false);
        HeadState head = await GetHeadStateAsync(repository, cancellationToken).ConfigureAwait(false);

        return new RepositoryRefState(refs, head, RefDecorationIndex.Build(refs, head));
    }

    /// <summary>
    /// Works out which multi-step operation a repository is in the middle of, from the marker
    /// files git leaves in the git directory.
    /// </summary>
    /// <param name="repository">The repository to inspect.</param>
    /// <returns>The operation in progress.</returns>
    public static RepositoryOperation DetectOperation(RepositoryHandle repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        // Order matters: a stopped rebase also leaves a REBASE_HEAD behind, and an interactive
        // rebase can leave a CHERRY_PICK_HEAD, so the rebase directories are checked first.
        if (Directory.Exists(repository.GetGitPath("rebase-merge")))
        {
            return RepositoryOperation.Rebase;
        }

        if (Directory.Exists(repository.GetGitPath("rebase-apply")))
        {
            return File.Exists(repository.GetGitPath(Path.Combine("rebase-apply", "applying")))
                ? RepositoryOperation.ApplyMailbox
                : RepositoryOperation.Rebase;
        }

        if (File.Exists(repository.GetGitPath("MERGE_HEAD")))
        {
            return RepositoryOperation.Merge;
        }

        if (File.Exists(repository.GetGitPath("CHERRY_PICK_HEAD")))
        {
            return RepositoryOperation.CherryPick;
        }

        if (File.Exists(repository.GetGitPath("REVERT_HEAD")))
        {
            return RepositoryOperation.Revert;
        }

        if (File.Exists(repository.GetGitPath("BISECT_LOG")))
        {
            return RepositoryOperation.Bisect;
        }

        return RepositoryOperation.None;
    }

    private static int CompareBranches(GitBranch left, GitBranch right)
    {
        // The checked-out branch always sorts first; everything else is alphabetical.
        if (left.IsCurrent != right.IsCurrent)
        {
            return left.IsCurrent ? -1 : 1;
        }

        return string.Compare(left.ShortName, right.ShortName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Reads remotes with <c>git remote --verbose</c>.
/// </summary>
public sealed class RemoteReader : IRemoteReader
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the read command.</param>
    /// <param name="commandFactory">Builds the read command.</param>
    public RemoteReader(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitRemote>> GetRemotesAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitResult result = await _runner.RunAsync(
                _commandFactory.Create(repository.WorkTreePath, "remote", "--verbose"),
                throwOnError: true,
                cancellationToken)
            .ConfigureAwait(false);

        return Parse(result.StandardOutput);
    }

    /// <summary>
    /// Parses the output of <c>git remote --verbose</c>.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <returns>The remotes, ordered by name.</returns>
    public static IReadOnlyList<GitRemote> Parse(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Dictionary<string, (string Fetch, string Push)> urls = new(StringComparer.Ordinal);
        List<string> order = [];

        foreach (string rawLine in payload.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            // "<name>\t<url> (fetch)" — the separator is a tab, and a URL may contain spaces.
            int tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            string name = line.Substring(0, tab);
            string rest = line.Substring(tab + 1);

            int lastSpace = rest.LastIndexOf(' ');
            if (lastSpace <= 0)
            {
                continue;
            }

            string url = rest.Substring(0, lastSpace);
            string direction = rest.Substring(lastSpace + 1).Trim('(', ')');

            if (!urls.TryGetValue(name, out (string Fetch, string Push) entry))
            {
                entry = (string.Empty, string.Empty);
                order.Add(name);
            }

            entry = direction switch
            {
                "push" => (entry.Fetch, url),
                _ => (url, entry.Push.Length == 0 ? url : entry.Push),
            };

            urls[name] = entry;
        }

        order.Sort(StringComparer.OrdinalIgnoreCase);

        List<GitRemote> remotes = new(order.Count);
        foreach (string name in order)
        {
            (string fetch, string push) = urls[name];
            remotes.Add(new GitRemote(name, fetch, push.Length == 0 ? fetch : push));
        }

        return remotes;
    }
}
