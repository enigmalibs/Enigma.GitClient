using System;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// What the history's search box finds a commit by: its message, the start of its SHA, and its
/// author's name or email (FEATURE-3E91).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistorySearchTests
{
    private const string Sha = "3fa9c07e1b2d4c5a6f7e8d9c0b1a2f3e4d5c6b7a";

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistorySearchTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static CommitRowViewModel Row(
        string subject = "Fix the token refresh",
        string body = "",
        string sha = Sha,
        string name = "Ada Lovelace",
        string email = "ada@example.com")
    {
        GitSignature author = new(name, email, DateTimeOffset.UnixEpoch);
        GitCommit commit = new(sha, [], author, author, subject, body);

        return new CommitRowViewModel(commit, new GraphRow(sha, 0, 0, false, true, [], 0), null, false, DateTimeOffset.UnixEpoch);
    }

    [Theory]
    [InlineData("3fa9c07")]
    [InlineData("3FA9C07")]
    [InlineData("3fa9")]
    [InlineData(Sha)]
    public void ASha_IsFoundByItsStart_InAnyCase(string search)
        => _fixture.Run(() => Assert.True(Row().Matches(search)));

    [Theory]
    [InlineData("c07e1b")]
    [InlineData("6b7a")]
    public void ASha_IsNotFoundByItsMiddleOrItsEnd(string search)
        => _fixture.Run(() => Assert.False(Row().Matches(search)));

    [Fact]
    public void AHexWord_DoesNotMarkACommitWhoseShaMerelyContainsIt()
    {
        _fixture.Run(() =>
        {
            // "add" is hex, and is in this SHA, not at its start.
            CommitRowViewModel row = Row(subject: "Rename the field", sha: "0000add00000000000000000000000000000beef");

            Assert.False(row.Matches("add"));
            Assert.False(row.Matches("beef"));
        });
    }

    [Theory]
    [InlineData("Ada")]
    [InlineData("lovelace")]
    [InlineData("Ada Lovelace")]
    [InlineData("ada@example.com")]
    [InlineData("EXAMPLE.COM")]
    [InlineData("@example")]
    public void AnAuthor_IsFoundByNameOrEmail_AnywhereInThem_InAnyCase(string search)
        => _fixture.Run(() => Assert.True(Row().Matches(search)));

    [Fact]
    public void AnotherAuthor_IsNotFound()
        => _fixture.Run(() => Assert.False(Row().Matches("grace")));

    [Theory]
    [InlineData("token")]
    [InlineData("TOKEN REFRESH")]
    [InlineData("expiry")]
    public void TheMessage_IsStillSearched_SubjectAndBody(string search)
        => _fixture.Run(() => Assert.True(Row(body: "Refresh it before its expiry.").Matches(search)));

    [Fact]
    public void AnEmptySearch_FindsNothing()
        => _fixture.Run(() => Assert.False(Row().Matches(string.Empty)));

    [Fact]
    public void TheUncommittedLine_MatchesNothing()
    {
        _fixture.Run(() =>
        {
            CommitRowViewModel uncommitted = CommitRowViewModel.Uncommitted(new GraphRow("WORKTREE", 0, 0, false, false, [], 0));

            Assert.False(uncommitted.Matches("uncommitted"));
            Assert.False(uncommitted.Matches("0"));
        });
    }
}
