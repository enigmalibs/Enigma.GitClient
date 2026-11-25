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

        GitRun<string> run = await RunProcessAsync(command, ReadOutputAsync, ReadErrorAsync, cancellationToken)
            .ConfigureAwait(false);

        return ToResult(command, run, throwOnError);
    }

    /// <inheritdoc />
    public async Task<GitRawResult> RunRawAsync(
        GitCommand command,
        bool throwOnError = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        GitRun<byte[]> run = await RunProcessAsync(command, ReadOutputBytesAsync, ReadErrorAsync, cancellationToken)
            .ConfigureAwait(false);

        GitRawResult result = new(run.ExitCode, run.StandardOutput, run.StandardError, run.Duration);

        if (throwOnError && !result.IsSuccess)
        {
            throw new GitCommandException(command, run.ExitCode, run.StandardError, string.Empty);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<GitResult> RunStreamingAsync(
        GitCommand command,
        IProgress<string>? standardErrorChunks,
        bool throwOnError = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        GitRun<string> run = await RunProcessAsync(
                command,
                ReadOutputAsync,
                (process, token) => ReadChunksAsync(process.StandardError, standardErrorChunks, token),
                cancellationToken)
            .ConfigureAwait(false);

        return ToResult(command, run, throwOnError);
    }

    /// <summary>
    /// Reads a stream chunk by chunk, reporting each one and accumulating the whole.
    /// </summary>
    /// <remarks>
    /// Chunks end at a newline <em>or</em> a carriage return: git overwrites its progress line in
    /// place with <c>\r</c>, so a reader that waits for <c>\n</c> sees a clone's progress only once
    /// the clone is over.
    /// </remarks>
    private static async Task<string> ReadChunksAsync(
        StreamReader reader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        StringBuilder all = new();
        StringBuilder current = new();
        char[] buffer = new char[1024];

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            for (int index = 0; index < read; index++)
            {
                char character = buffer[index];
                all.Append(character);

                if (character is '\r' or '\n')
                {
                    if (current.Length > 0)
                    {
                        progress?.Report(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(character);
            }
        }

        if (current.Length > 0)
        {
            progress?.Report(current.ToString());
        }

        return all.ToString();
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

    /// <summary>
    /// Runs git once: starts it, hands its standard input over, reads both of its outputs to the end,
    /// waits for it to exit and logs how it went — and kills it when the run is cancelled.
    /// </summary>
    /// <typeparam name="TOutput">What the standard output is read as: text, or the raw bytes.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="readOutput">Reads the standard output to its end.</param>
    /// <param name="readError">Reads the standard error to its end.</param>
    /// <param name="cancellationToken">Cancels the run, and kills the process.</param>
    /// <returns>What the run came to.</returns>
    /// <remarks>
    /// Every public entry point goes through here and differs only in its two readers, so the shared
    /// part — the hand-over of standard input, the kill on cancellation, the completion log — exists
    /// once. Both readers start before standard input is written: a process that fills a pipe nobody
    /// reads blocks, and with it the write.
    /// </remarks>
    private async Task<GitRun<TOutput>> RunProcessAsync<TOutput>(
        GitCommand command,
        Func<Process, CancellationToken, Task<TOutput>> readOutput,
        Func<Process, CancellationToken, Task<string>> readError,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        using Process process = StartProcess(command);

        try
        {
            Task<TOutput> outputTask = readOutput(process, cancellationToken);
            Task<string> errorTask = readError(process, cancellationToken);

            await WriteStandardInputAsync(process, command, cancellationToken).ConfigureAwait(false);

            TOutput standardOutput = await outputTask.ConfigureAwait(false);
            string standardError = await errorTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            TimeSpan duration = Stopwatch.GetElapsedTime(startedAt);
            LogCompletion(command, process.ExitCode, duration);

            return new GitRun<TOutput>(process.ExitCode, standardOutput, standardError, duration);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }
    }

    private static Task<string> ReadOutputAsync(Process process, CancellationToken cancellationToken)
        => process.StandardOutput.ReadToEndAsync(cancellationToken);

    private static Task<string> ReadErrorAsync(Process process, CancellationToken cancellationToken)
        => process.StandardError.ReadToEndAsync(cancellationToken);

    private static async Task<byte[]> ReadOutputBytesAsync(Process process, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    /// <summary>
    /// The text result of a run, or the exception that says it failed.
    /// </summary>
    private static GitResult ToResult(GitCommand command, GitRun<string> run, bool throwOnError)
    {
        GitResult result = new(run.ExitCode, run.StandardOutput, run.StandardError, run.Duration);

        if (throwOnError && !result.IsSuccess)
        {
            throw new GitCommandException(command, run.ExitCode, run.StandardError, run.StandardOutput);
        }

        return result;
    }

    /// <summary>
    /// What one run of git came to, before it is shaped into a result.
    /// </summary>
    /// <typeparam name="TOutput">What the standard output was read as.</typeparam>
    private readonly record struct GitRun<TOutput>(int ExitCode, TOutput StandardOutput, string StandardError, TimeSpan Duration);

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
            // The native error goes along as the inner exception: "not found", "access denied" and
            // "not a program" are three different fixes, and only its code tells them apart.
            throw new GitNotFoundException(
                $"The git executable at '{_executable.Path}' could not be started: {exception.Message}",
                exception);
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
