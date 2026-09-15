using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Probes the host's git installation once and caches the answer.
/// </summary>
public sealed class GitEnvironment : IGitEnvironment
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IGitExecutable _executable;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GitAvailability? _cached;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the probe.</param>
    /// <param name="commandFactory">Builds the probe command.</param>
    /// <param name="executable">Reports which executable is being probed.</param>
    public GitEnvironment(IGitProcessRunner runner, IGitCommandFactory commandFactory, IGitExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(executable);

        _runner = runner;
        _commandFactory = commandFactory;
        _executable = executable;
    }

    /// <inheritdoc />
    public async Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        GitAvailability availability = await GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        return availability.Version
            ?? throw new GitNotFoundException(availability.Message);
    }

    /// <inheritdoc />
    public async Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cached ??= await ProbeAsync(cancellationToken).ConfigureAwait(false);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GitAvailability> ProbeAsync(CancellationToken cancellationToken)
    {
        string? executablePath = _executable.TryResolve(out string? resolved) ? resolved : null;

        // The probe runs in a directory that always exists and is never a repository, so a broken
        // repository can never make the version check fail.
        string workingDirectory = AppContext.BaseDirectory;

        try
        {
            GitCommand command = _commandFactory.Create(workingDirectory, "--version");
            GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess || !GitVersion.TryParse(result.TrimmedOutput, out GitVersion version))
            {
                return new GitAvailability(
                    GitAvailabilityStatus.NotFound,
                    Version: null,
                    executablePath,
                    "The git executable did not report a usable version. Install git 2.20 or newer.");
            }

            if (version < GitVersion.Minimum)
            {
                return new GitAvailability(
                    GitAvailabilityStatus.TooOld,
                    version,
                    executablePath,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "git {0} is installed, but Enigma.GitClient needs {1} or newer.",
                        version,
                        GitVersion.Minimum));
            }

            return new GitAvailability(GitAvailabilityStatus.Available, version, executablePath, string.Empty);
        }
        catch (GitNotFoundException exception)
        {
            return new GitAvailability(GitAvailabilityStatus.NotFound, Version: null, executablePath, exception.Message);
        }
    }
}
