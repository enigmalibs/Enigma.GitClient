using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

/// <summary>
/// A git that cannot be started says why, with the operating system's own error kept (EXC-02).
/// </summary>
public sealed class GitNotFoundExceptionTests
{
    [Fact]
    public void TheStandardConstructors_AreThere()
    {
        Win32Exception cause = new(5);

        Assert.False(string.IsNullOrEmpty(new GitNotFoundException().Message));
        Assert.Equal("missing", new GitNotFoundException("missing").Message);
        Assert.Same(cause, new GitNotFoundException("denied", cause).InnerException);
    }

    [Fact]
    public async Task AnExecutableThatCannotBeStarted_KeepsTheNativeErrorAsTheInnerException()
    {
        // A rooted path where nothing is: the start fails in the operating system, not in the lookup.
        string missing = Path.Combine(Path.GetTempPath(), "enigma-no-git-" + Guid.NewGuid().ToString("N"), "git.exe");
        GitExecutableOptions options = new() { ExecutablePath = missing };
        GitProcessRunner runner = new(
            new GitExecutable(Options.Create(options)),
            Options.Create(options),
            NullLogger<GitProcessRunner>.Instance);

        GitNotFoundException exception = await Assert.ThrowsAsync<GitNotFoundException>(
            () => runner.RunAsync(
                new GitCommandFactory().Create(Path.GetTempPath(), "--version"),
                cancellationToken: TestContext.Current.CancellationToken));

        Win32Exception cause = Assert.IsType<Win32Exception>(exception.InnerException);
        Assert.NotEqual(0, cause.NativeErrorCode);
        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }
}
