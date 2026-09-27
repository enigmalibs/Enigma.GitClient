using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Core.Sync;
using Enigma.GitClient.Core.Tags;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// Every operation that reaches a remote signs in with what the resolver gives, and nothing else does.
/// </summary>
public sealed class SignedInOperationsTests
{
    private const string Token = "ghp_0123456789abcdefghijklmnopqrstuvwxyz";

    private static readonly RepositoryHandle Repository =
        new(AppContext.BaseDirectory, System.IO.Path.Combine(AppContext.BaseDirectory, ".git"));

    private readonly RecordingRunner _runner = new();
    private readonly FixedCredentials _credentials = new(GitHub());
    private readonly GitCommandFactory _factory = new();

    private static GitCredentials GitHub()
    {
        Assert.True(GitHostCredential.TryCreate(
            new Uri("https://github.com"), "x-access-token", new SecretString(Token), out GitHostCredential? credential));

        return GitCredentials.For([credential!]);
    }

    private SyncService Sync() => new(_runner, _factory, _credentials);

    private TagService Tags() => new(_runner, _factory, _credentials);

    private BranchService Branches() => new(_runner, _factory, new UnusedRefReader(), _credentials);

    private static void AssertSignedIn(GitCommand command, string verb)
    {
        Assert.Equal(verb, command.Verb);
        Assert.Equal(["-c", "credential.https://github.com.helper="], command.Arguments.Take(2));
        Assert.Equal(Token, command.Environment["ENIGMA_GIT_PASSWORD_0"]);
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fetch_Pull_FastForward_AndPush_SignIn()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SyncService sync = Sync();

        await sync.FetchAsync(Repository, cancellationToken: cancellationToken);
        await sync.PullAsync(Repository, cancellationToken: cancellationToken);
        await sync.FastForwardBranchAsync(Repository, "origin", "main", "main", cancellationToken: cancellationToken);
        await sync.PushAsync(Repository, new PushRequest(), cancellationToken: cancellationToken);

        Assert.Collection(
            _runner.Commands,
            command => AssertSignedIn(command, "fetch"),
            command => AssertSignedIn(command, "pull"),
            command => AssertSignedIn(command, "fetch"),
            command => AssertSignedIn(command, "push"));
        Assert.All(_credentials.AskedFor, repository => Assert.Same(Repository, repository));
    }

    [Fact]
    public async Task WithoutALogin_TheCommandsAreExactlyTodays()
    {
        _credentials.Answer = GitCredentials.None;

        await Sync().PushAsync(Repository, new PushRequest(), cancellationToken: TestContext.Current.CancellationToken);

        GitCommand command = Assert.Single(_runner.Commands);
        Assert.Equal(
            _factory.Create(Repository.WorkTreePath, SyncService.BuildPushArguments(new PushRequest())).Arguments,
            command.Arguments);
        Assert.Empty(command.Environment);
    }

    [Fact]
    public async Task PushingAndDeletingATagOnARemote_SignIn()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Tags().PushAsync(Repository, "origin", "v1.0.0", cancellationToken);
        await Tags().DeleteRemoteAsync(Repository, "origin", "v1.0.0", cancellationToken);

        Assert.Collection(
            _runner.Commands,
            command => AssertSignedIn(command, "push"),
            command => AssertSignedIn(command, "push"));
    }

    [Fact]
    public async Task ATagMadeHere_DoesNotAskForALogin()
    {
        await Tags().DeleteAsync(Repository, "v1.0.0", TestContext.Current.CancellationToken);

        Assert.Empty(_credentials.AskedFor);
        Assert.Equal("tag", Assert.Single(_runner.Commands).Verb);
    }

    [Fact]
    public async Task DeletingABranchOnARemote_SignsIn()
    {
        await Branches().DeleteRemoteAsync(Repository, "origin", "feature", TestContext.Current.CancellationToken);

        AssertSignedIn(Assert.Single(_runner.Commands), "push");
    }

    /// <summary>Gives the same credentials every time, and remembers who asked.</summary>
    private sealed class FixedCredentials : IGitCredentialResolver
    {
        public FixedCredentials(GitCredentials answer) => Answer = answer;

        public GitCredentials Answer { get; set; }

        public List<RepositoryHandle> AskedFor { get; } = [];

        public Task<GitCredentials> ForRepositoryAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        {
            AskedFor.Add(repository);
            return Task.FromResult(Answer);
        }
    }

    /// <summary>Records every command and succeeds without running anything.</summary>
    private sealed class RecordingRunner : IGitProcessRunner
    {
        private static readonly GitResult Success = new(0, string.Empty, string.Empty, TimeSpan.Zero);

        public List<GitCommand> Commands { get; } = [];

        public Task<GitResult> RunAsync(GitCommand command, bool throwOnError = true, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(Success);
        }

        public Task<GitResult> RunStreamingAsync(
            GitCommand command,
            IProgress<string>? standardErrorChunks,
            bool throwOnError = true,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(Success);
        }

        public Task<GitRawResult> RunRawAsync(GitCommand command, bool throwOnError = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> RunLinesAsync(GitCommand command, char separator = '\n', CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>A remote branch's deletion reads no refs.</summary>
    private sealed class UnusedRefReader : IRefReader
    {
        public Task<RefCollection> GetRefsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HeadState> GetHeadStateAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RepositoryRefState> GetStateAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
