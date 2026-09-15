using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// A deliberately minimal git runner used only to build test fixtures. It does not go through the
/// production <c>GitProcessRunner</c>, so a bug in the code under test can never quietly corrupt
/// the fixture it is being tested against.
/// </summary>
internal static class GitCli
{
    public static async Task<string> RunAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> arguments)
    {
        System.Threading.CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> entry in environment)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git could not be started.");

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        string output = await outputTask;
        string error = await errorTask;
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Fixture command 'git {string.Join(' ', arguments)}' failed with {process.ExitCode}: {error}");
        }

        return output;
    }
}
