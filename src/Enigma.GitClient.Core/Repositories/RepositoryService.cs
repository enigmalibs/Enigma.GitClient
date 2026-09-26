using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// Default <see cref="IRepositoryService"/>.
/// </summary>
public sealed class RepositoryService : IRepositoryService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IRepositoryLocator _locator;
    private readonly ILogger<RepositoryService> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="locator">Discovers the repository once it exists.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public RepositoryService(
        IGitProcessRunner runner,
        IGitCommandFactory commandFactory,
        IRepositoryLocator locator,
        ILogger<RepositoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _commandFactory = commandFactory;
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<RepositoryDiscoveryResult> OpenAsync(string path, CancellationToken cancellationToken = default)
        => _locator.DiscoverAsync(path, cancellationToken);

    /// <inheritdoc />
    public async Task<RepositoryHandle> InitAsync(
        string path,
        string initialBranch = "main",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialBranch);

        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(full);

        await _runner.RunAsync(
                _commandFactory.Create(full, "init", "."),
                throwOnError: true,
                cancellationToken)
            .ConfigureAwait(false);

        // "git init -b" only exists from git 2.28; setting the symbolic ref works on every version
        // the client supports and produces exactly the same result on an empty repository.
        await _runner.RunAsync(
                _commandFactory.Create(full, "symbolic-ref", "HEAD", $"refs/heads/{initialBranch}"),
                throwOnError: true,
                cancellationToken)
            .ConfigureAwait(false);

        RepositoryDiscoveryResult discovered = await _locator.DiscoverAsync(full, cancellationToken)
            .ConfigureAwait(false);

        return discovered.Repository
            ?? throw new InvalidOperationException(
                $"The repository was created at '{full}' but could not be opened: {discovered.Message}");
    }

    /// <inheritdoc />
    public async Task<RepositoryHandle> CloneAsync(
        CloneRequest request,
        IProgress<CloneProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RemoteUrlValidation validation = ValidateCloneUrl(request.Url);

        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Message, nameof(request));
        }

        string target = Path.GetFullPath(request.TargetPath);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).GetEnumerator().MoveNext())
        {
            throw new ArgumentException($"'{target}' already exists and is not empty.", nameof(request));
        }

        Directory.CreateDirectory(request.ParentDirectory);

        progress?.Report(CloneProgress.Starting);

        List<string> arguments = ["clone", "--progress", "--origin", request.RemoteName];

        if (request.Branch is { Length: > 0 } branch)
        {
            arguments.Add("--branch");
            arguments.Add(branch);
        }

        if (request.Depth is { } depth && depth > 0)
        {
            arguments.Add($"--depth={depth.ToString(CultureInfo.InvariantCulture)}");
        }

        if (request.RecurseSubmodules)
        {
            arguments.Add("--recurse-submodules");
        }

        arguments.Add("--");
        arguments.Add(validation.NormalisedUrl);
        arguments.Add(target);

        GitCommand command = _commandFactory.Create(request.ParentDirectory, arguments);

        Progress<string>? relay = progress is null
            ? null
            : new Progress<string>(chunk =>
            {
                CloneProgress? report = CloneProgressParser.Parse(chunk);

                if (report is not null)
                {
                    progress.Report(report);
                }
            });

        try
        {
            await _runner.RunStreamingAsync(command, relay, throwOnError: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or GitCommandException)
        {
            // A half-written clone is worse than none: the user would have to find and delete it
            // before they could retry.
            RemoveQuietly(target);
            throw;
        }

        progress?.Report(new CloneProgress(CloneStage.Done, 100, "Done."));

        RepositoryDiscoveryResult discovered = await _locator.DiscoverAsync(target, cancellationToken)
            .ConfigureAwait(false);

        return discovered.Repository
            ?? throw new InvalidOperationException(
                $"The repository was cloned into '{target}' but could not be opened: {discovered.Message}");
    }

    /// <inheritdoc />
    public RemoteUrlValidation ValidateCloneUrl(string? url) => RemoteUrlValidator.Validate(url);

    private void RemoveQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The partial clone at {Path} could not be removed", path);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "The partial clone at {Path} could not be removed", path);
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        // git marks everything under .git/objects read-only, which makes a plain recursive delete
        // fail on Windows.
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(file);

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
