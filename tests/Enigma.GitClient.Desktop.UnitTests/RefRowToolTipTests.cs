using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// What a compact line of the tags or the remotes page says when the pointer rests on it: everything
/// the line itself no longer shows.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RefRowToolTipTests
{
    private const string Sha = "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d";

    private static readonly DateTimeOffset Written = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly HeadlessAvaloniaFixture _fixture;

    public RefRowToolTipTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TagRowViewModel Row(GitTag tag)
    {
        AsyncRelayCommand<TagRowViewModel> nothing = new(_ => Task.CompletedTask);
        return new TagRowViewModel(tag, nothing, nothing);
    }

    [Fact]
    public void AnAnnotatedTag_SaysItsKindItsMessageItsTaggerAndItsCommit()
    {
        TagRowViewModel row = Row(new GitTag(
            "refs/tags/v6.0.0",
            Sha,
            "fedcba9876543210fedcba9876543210fedcba98",
            new GitSignature("Ada Lovelace", "ada@example.com", Written),
            "Release 6.0.0\n\nThe watcher and the compact lists.\n",
            Written));

        Assert.Equal(
            $"Annotated tag\nRelease 6.0.0\n\nThe watcher and the compact lists.\nTagged by Ada Lovelace\n1a2b3c4 · {row.Date}",
            row.ToolTip);
    }

    [Fact]
    public void ALightweightTag_SaysItsKindAndItsCommit()
    {
        TagRowViewModel row = Row(new GitTag("refs/tags/nightly", Sha, null, null, string.Empty, Written));

        Assert.Equal($"Lightweight tag\n1a2b3c4 · {row.Date}", row.ToolTip);
    }

    [Theory]
    [InlineData("https://github.com/enigmalibs/gitclient.git", "https://github.com/enigmalibs/gitclient.git", "github.com")]
    [InlineData("https://github.com/enigmalibs/gitclient.git", "git@github.com:enigmalibs/gitclient.git", "github.com\nPushes to git@github.com:enigmalibs/gitclient.git")]
    [InlineData("/srv/git/gitclient.git", "/srv/git/gitclient.git", "/srv/git/gitclient.git")]
    public void ARemote_SaysItsHost_AndWherePushesGoWhenThatIsElsewhere(string fetchUrl, string pushUrl, string expected)
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            RemoteRowViewModel row = new(services.Get<RemotesPageViewModel>(), new GitRemote("origin", fetchUrl, pushUrl), 2);

            Assert.Equal(expected, row.ToolTip);
        });
    }
}
