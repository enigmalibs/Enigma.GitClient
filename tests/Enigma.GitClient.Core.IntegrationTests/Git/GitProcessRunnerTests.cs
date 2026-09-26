using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Git;

public sealed class GitProcessRunnerTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync();
        await _repository.CommitInitialAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IGitProcessRunner Runner => _host.GetRequiredService<IGitProcessRunner>();

    private IGitCommandFactory Factory => _host.GetRequiredService<IGitCommandFactory>();

    [Fact]
    public async Task RunAsync_ReturnsStandardOutputAndASuccessfulExitCode()
    {
        GitResult result = await Runner.RunAsync(
            Factory.Create(_repository.Path, "rev-parse", "--is-inside-work-tree"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("true", result.TrimmedOutput);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_ThrowsWithTheExitCodeAndStandardErrorWhenGitFails()
    {
        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => Runner.RunAsync(
                Factory.Create(_repository.Path, "rev-parse", "--verify", "refs/heads/does-not-exist"),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotEqual(0, exception.ExitCode);
        Assert.Equal("rev-parse", exception.Verb);
        Assert.Equal(_repository.Path, exception.WorkingDirectory);
        Assert.Contains("does-not-exist", string.Join(' ', exception.RedactedArguments), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReturnsTheFailureInsteadOfThrowingWhenAsked()
    {
        GitResult result = await Runner.RunAsync(
            Factory.Create(_repository.Path, "rev-parse", "--verify", "refs/heads/does-not-exist"),
            throwOnError: false,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task RunAsync_UsesAnArgumentVectorSoNothingIsInterpretedByAShell()
    {
        // A file name that a shell would mangle beyond recognition survives verbatim.
        const string awkward = "a file; rm -rf $(echo nothing) 'quoted'.txt";
        _repository.WriteFile(awkward, "content\n");

        await Runner.RunAsync(
            Factory.Create(_repository.Path, "add", "--", awkward),
            cancellationToken: TestContext.Current.CancellationToken);

        GitResult staged = await Runner.RunAsync(
            Factory.Create(_repository.Path, "diff", "--cached", "--name-only"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(awkward, staged.SplitOutput());
    }

    [Fact]
    public async Task RunAsync_EmitsRawUtf8PathsRatherThanCStyleEscapes()
    {
        const string accented = "rapport-financier-écrit.txt";
        _repository.WriteFile(accented, "content\n");

        await Runner.RunAsync(
            Factory.Create(_repository.Path, "add", "--", accented),
            cancellationToken: TestContext.Current.CancellationToken);

        GitResult staged = await Runner.RunAsync(
            Factory.Create(_repository.Path, "diff", "--cached", "--name-only"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(accented, staged.SplitOutput());
    }

    [Fact]
    public async Task RunAsync_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner.RunAsync(Factory.Create(_repository.Path, "log"), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task RunRawAsync_ReturnsTheExactBytesOfABlob()
    {
        byte[] expected = [0x00, 0x01, 0x02, 0xFF, 0xFE, 0x0A];
        await System.IO.File.WriteAllBytesAsync(
            _repository.GetPath("binary.bin"),
            expected,
            TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Add a binary file");

        GitRawResult result = await Runner.RunRawAsync(
            Factory.Create(_repository.Path, "show", "HEAD:binary.bin"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public async Task RunLinesAsync_SplitsNulSeparatedOutput()
    {
        _repository.WriteFile("one.txt", "1\n");
        _repository.WriteFile("two.txt", "2\n");
        await _repository.CommitAllAsync("Add two files");

        System.Collections.Generic.IReadOnlyList<string> lines = await Runner.RunLinesAsync(
            Factory.Create(_repository.Path, "ls-tree", "-r", "--name-only", "-z", "HEAD"),
            '\0',
            TestContext.Current.CancellationToken);

        Assert.Contains("one.txt", lines);
        Assert.Contains("two.txt", lines);
    }

    [Fact]
    public async Task RunAsync_RefusesToRebase()
        => await Assert.ThrowsAsync<NotSupportedException>(
            () => Task.FromResult(Factory.Create(_repository.Path, "rebase", "main")));

    [Fact]
    public async Task RunAsync_WritesStandardInputWhenTheCommandSuppliesIt()
    {
        GitResult result = await Runner.RunAsync(
            Factory.CreateWithInput(_repository.Path, "hello from stdin\n", ["hash-object", "-w", "--stdin"]),
            cancellationToken: TestContext.Current.CancellationToken);

        string sha = result.TrimmedOutput;
        Assert.Equal(40, sha.Length);

        GitResult content = await Runner.RunAsync(
            Factory.Create(_repository.Path, "cat-file", "-p", sha),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello from stdin", content.TrimmedOutput);
    }
}
