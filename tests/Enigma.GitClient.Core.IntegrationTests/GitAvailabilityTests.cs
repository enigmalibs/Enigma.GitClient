using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests;

/// <summary>
/// Smoke test proving the integration suite can reach a real <c>git</c> executable. Every other
/// test in this project relies on it.
/// </summary>
public sealed class GitAvailabilityTests
{
    [Fact]
    public async Task Git_IsOnThePath()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--version");

        using Process process = Process.Start(startInfo)!;
        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.StartsWith("git version", output);
    }
}
