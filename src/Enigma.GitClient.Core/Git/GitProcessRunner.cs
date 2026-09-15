using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Runs git as a child process. Arguments are always passed as a vector, never as a command line,
/// and the child's environment is normalised so output is parseable and no invocation can block on
/// an invisible terminal prompt.
/// </summary>
public sealed class GitProcessRunner : IGitProcessRunner
{
    private static readonly UTF8Encoding OutputEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IGitExecutable _executable;
    private readonly IOptions<GitExecutableOptions> _options;
    private readonly ILogger<GitProcessRunner> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="executable">Locates the git executable.</param>
    /// <param name="options">Supplies the environment overrides applied to every child process.</param>
    /// <param name="logger">Receives one debug entry per invocation, with credentials redacted.</param>
    public GitProcessRunner(
        IGitExecutable executable,
        IOptions<GitExecutableOptions> options,
        ILogger<GitProcessRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _executable = executable;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GitResult> RunAsync(
        GitCommand command,
        bool throwOnError = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (int exitCode, string standardOutput, string standardError, TimeSpan duration) =
            await RunCoreAsync(command, cancellationToken).ConfigureAwait(false);

        GitResult result = new(exitCode, standardOutput, standardError, duration);

        if (throwOnError && !result.IsSuccess)
        {
            throw new GitCommandException(command, exitCode, standardError, standardOutput);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<GitRawResult> RunRawAsync(
        GitCommand command,
        bool throwOnError = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (int exitCode, byte[] standardOutput, string standardError, TimeSpan duration) =
            await RunRawCoreAsync(command, cancellationToken).ConfigureAwait(false);

        GitRawResult result = new(exitCode, standardOutput, standardError, duration);

        if (throwOnError && !result.IsSuccess)
        {
            throw new GitCommandException(command, exitCode, standardError, string.Empty);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> RunLinesAsync(
        GitCommand command,
        char separator = '\n',
        CancellationToken cancellationToken = default)
    {
        GitResult result = await RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
        return result.SplitOutput(separator);
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)> RunCoreAsync(
        GitCommand command,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        using Process process = StartProcess(command);

        try
        {
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await WriteStandardInputAsync(process, command, cancellationToken).ConfigureAwait(false);

            string standardOutput = await outputTask.ConfigureAwait(false);
            string standardError = await errorTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            TimeSpan duration = Stopwatch.GetElapsedTime(startedAt);
            LogCompletion(command, process.ExitCode, duration);

            return (process.ExitCode, standardOutput, standardError, duration);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }
    }

    private async Task<(int ExitCode, byte[] StandardOutput, string StandardError, TimeSpan Duration)> RunRawCoreAsync(
        GitCommand command,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        using Process process = StartProcess(command);

        try
        {
            using MemoryStream buffer = new();
            Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await WriteStandardInputAsync(process, command, cancellationToken).ConfigureAwait(false);

            await copyTask.ConfigureAwait(false);
            string standardError = await errorTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            TimeSpan duration = Stopwatch.GetElapsedTime(startedAt);
            LogCompletion(command, process.ExitCode, duration);

            return (process.ExitCode, buffer.ToArray(), standardError, duration);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }
    }

    private Process StartProcess(GitCommand command)
    {
        ForbiddenGitOperations.EnsureAllowed(command.Arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = _executable.Path,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = command.StandardInput is not null,
            StandardOutputEncoding = OutputEncoding,
            StandardErrorEncoding = OutputEncoding,
        };

        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyEnvironment(startInfo, command, _options.Value);

        _logger.LogDebug("Running {Command} in {WorkingDirectory}", command.ToString(), command.WorkingDirectory);

        try
        {
            return Process.Start(startInfo)
                ?? throw new GitNotFoundException($"The git process at '{_executable.Path}' could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new GitNotFoundException(
                $"The git executable at '{_executable.Path}' could not be started: {exception.Message}");
        }
    }

    private static void ApplyEnvironment(
        ProcessStartInfo startInfo,
        GitCommand command,
        GitExecutableOptions options)
    {
        // English, parseable messages regardless of the user's locale.
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        // A desktop application must never block on a terminal prompt it cannot show. GUI credential
        // helpers are deliberately left alone: they raise their own window and still work.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        // Read-only commands must not fight the user's editor for the index lock.
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        // Output is redirected, so a pager would only ever be a way to hang.
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";

        foreach (KeyValuePair<string, string> entry in options.EnvironmentOverrides)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }

        // A per-command value always wins over the application-wide override.
        foreach (KeyValuePair<string, string> entry in command.Environment)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }
    }

    private static async Task WriteStandardInputAsync(
        Process process,
        GitCommand command,
        CancellationToken cancellationToken)
    {
        if (command.StandardInput is null)
        {
            return;
        }

        await process.StandardInput.WriteAsync(command.StandardInput.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private void LogCompletion(GitCommand command, int exitCode, TimeSpan duration)
        => _logger.LogDebug(
            "git {Verb} exited with {ExitCode} in {ElapsedMilliseconds} ms",
            command.Verb,
            exitCode,
            duration.TotalMilliseconds);

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited between the check and the kill.
        }
        catch (NotSupportedException)
        {
            // Killing a process tree is unsupported on this platform; the child will exit on its own.
        }
        catch (Win32Exception)
        {
            // The operating system refused the kill; nothing useful can be done here.
        }
    }
}
