using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// Discovers repositories by asking git itself, so worktrees, submodules and
/// <c>GIT_DIR</c>-style layouts all resolve the way git resolves them.
/// </summary>
public sealed class RepositoryLocator : IRepositoryLocator
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the discovery command.</param>
    /// <param name="commandFactory">Builds the discovery command.</param>
    public RepositoryLocator(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task<RepositoryDiscoveryResult> DiscoverAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string probeDirectory = ResolveProbeDirectory(path);
        if (probeDirectory.Length == 0)
        {
            return RepositoryDiscoveryResult.NotARepository(path);
        }

        // Two calls on purpose: --show-toplevel fails outright inside a bare repository, so the
        // bare flag and the git directory (both of which a bare repository answers) are read first.
        GitCommand probe = _commandFactory.Create(
            probeDirectory,
            "rev-parse",
            "--is-bare-repository",
            "--absolute-git-dir");

        GitResult probeResult = await _runner.RunAsync(probe, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string> probeLines = probeResult.SplitOutput();
        if (!probeResult.IsSuccess || probeLines.Count < 2)
        {
            return RepositoryDiscoveryResult.NotARepository(path);
        }

        if (string.Equals(probeLines[0], "true", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryDiscoveryResult.Bare(path);
        }

        string gitDirectory = probeLines[1];

        GitCommand topLevel = _commandFactory.Create(probeDirectory, "rev-parse", "--show-toplevel");
        GitResult topLevelResult = await _runner.RunAsync(topLevel, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (!topLevelResult.IsSuccess || topLevelResult.TrimmedOutput.Length == 0)
        {
            // A repository with a git directory but no work tree is, for our purposes, bare.
            return RepositoryDiscoveryResult.Bare(path);
        }

        return RepositoryDiscoveryResult.Found(
            new RepositoryHandle(topLevelResult.TrimmedOutput, gitDirectory));
    }

    private static string ResolveProbeDirectory(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);

            if (Directory.Exists(full))
            {
                return full;
            }

            if (File.Exists(full))
            {
                return Path.GetDirectoryName(full) ?? string.Empty;
            }
        }
        catch (ArgumentException)
        {
            // A malformed path is simply not a repository.
        }
        catch (NotSupportedException)
        {
            // Same: an unsupported path format is not a repository.
        }
        catch (PathTooLongException)
        {
            // Same.
        }

        return string.Empty;
    }
}
