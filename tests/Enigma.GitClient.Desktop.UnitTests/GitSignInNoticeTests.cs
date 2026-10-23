using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Desktop.Navigation;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The repository window's notice that git signed in on its own because a stored token could not be
/// read (BUG-3024): said once, not at every automatic fetch; offering the reset only when the whole store
/// is unreadable; and gone once the problem is resolved.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class GitSignInNoticeTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public GitSignInNoticeTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices Build()
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IGitCredentialResolver>();
            services.AddSingleton<IGitCredentialResolver, FakeResolver>();
        });

    private static HostAccount Account(string userName = "octocat", string uri = "https://github.com")
        => HostAccount.Create(HostKind.GitHub, new Uri(uri), userName, userName, "work");

    private static void Settle()
    {
        for (int pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>A fetch whose integration's token could not be read, as the resolver reports it.</summary>
    private static void FetchFails(TestServices services, HostAccount account, TokenProtectionReason reason)
    {
        ((FakeResolver)services.Get<IGitCredentialResolver>()).Raise(account, reason);
        Settle();
    }

    [Fact]
    public void RepeatedFetches_SayItOnce_AndADismissedNoticeStaysAway()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();
            GitSignInNoticeViewModel notice = services.Get<GitSignInNoticeViewModel>();
            HostAccount account = Account();

            Assert.False(notice.IsVisible);

            FetchFails(services, account, TokenProtectionReason.EntryUnreadable);

            Assert.True(notice.IsVisible);
            Assert.Contains("The stored token for github.com could not be read", notice.Message, StringComparison.Ordinal);
            Assert.Contains("git is using its own sign-in", notice.Message, StringComparison.Ordinal);
            Assert.False(notice.OffersReset);

            notice.DismissCommand.Execute(null);

            // The automatic refresh fetches again, and again: nothing more is said this session.
            FetchFails(services, account, TokenProtectionReason.EntryUnreadable);
            FetchFails(services, account, TokenProtectionReason.EntryUnreadable);

            Assert.False(notice.IsVisible);
        });
    }

    [Fact]
    public void AStoreThatCannotBeRead_OffersTheReset_AndIsSaidOnceForEveryIntegration()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();
            GitSignInNoticeViewModel notice = services.Get<GitSignInNoticeViewModel>();

            FetchFails(services, Account(), TokenProtectionReason.KeyUnavailable);

            Assert.True(notice.IsVisible);
            Assert.True(notice.OffersReset);
            Assert.Contains("reset them", notice.Message, StringComparison.Ordinal);

            notice.DismissCommand.Execute(null);
            FetchFails(services, Account("hubot", "https://gitlab.example.com"), TokenProtectionReason.KeyUnavailable);

            Assert.False(notice.IsVisible);
        });
    }

    [Fact]
    public void TheReset_PutsTheNoticeAway_AndForgetsItWasSaid()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            GitSignInNoticeViewModel notice = services.Get<GitSignInNoticeViewModel>();
            FetchFails(services, Account(), TokenProtectionReason.KeyUnavailable);

            services.Dialogs.Script(DialogResult.Primary);
            await notice.ResetCommand.ExecuteAsync(null);
            Settle();

            Assert.Equal(TokenRecovery.ResetLabel, Assert.Single(services.Dialogs.Shown).Title);
            Assert.False(notice.IsVisible);

            // Should it fail again after that, it is news again.
            FetchFails(services, Account(), TokenProtectionReason.KeyUnavailable);
            Assert.True(notice.IsVisible);
        });
    }

    [Fact]
    public void TheIntegrationsTokenStoredAgain_PutsItsNoticeAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            GitSignInNoticeViewModel notice = services.Get<GitSignInNoticeViewModel>();
            HostAccount account = Account();
            FetchFails(services, account, TokenProtectionReason.EntryUnreadable);

            // Another integration's token says nothing about this one.
            await services.Get<ITokenStore>().SetAsync(Account("hubot").TokenKey, new SecretString("ghp_other"));
            Settle();
            Assert.True(notice.IsVisible);

            await services.Get<ITokenStore>().SetAsync(account.TokenKey, new SecretString("ghp_new"));
            Settle();

            Assert.False(notice.IsVisible);
        });
    }

    [Fact]
    public void ATokenStoredAgain_InAStoreThatCouldNotBeRead_PutsTheNoticeAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            GitSignInNoticeViewModel notice = services.Get<GitSignInNoticeViewModel>();
            FetchFails(services, Account(), TokenProtectionReason.KeyUnavailable);

            await services.Get<ITokenStore>().SetAsync(Account("hubot").TokenKey, new SecretString("ghp_other"));
            Settle();

            Assert.False(notice.IsVisible);
        });
    }

    [Fact]
    public void OpenProfiles_GoesToTheProfilesPage()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();
            GitSignInNoticeViewModel notice = services.Get<GitSignInNoticeViewModel>();

            notice.OpenProfilesCommand.Execute(null);

            Assert.Equal(ShellPage.Profiles, services.Get<IShellNavigation>().Current);
        });
    }

    [Fact]
    public void TheRepositoryWindow_CarriesTheNotice()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();

            Assert.Same(services.Get<GitSignInNoticeViewModel>(), services.Get<MainWindowViewModel>().SignInNotice);
        });
    }

    /// <summary>A resolver whose only behaviour is the event, raised when a test says.</summary>
    private sealed class FakeResolver : IGitCredentialResolver
    {
        public event EventHandler<TokenUnreadableEventArgs>? TokenUnreadable;

        public void Raise(HostAccount account, TokenProtectionReason reason)
            => TokenUnreadable?.Invoke(this, new TokenUnreadableEventArgs(account, account.BaseUri.GetLeftPart(UriPartial.Authority), reason));

        public Task<GitCredentials> ForRepositoryAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => Task.FromResult(GitCredentials.None);

        public Task<GitCredentials> ForCloneAsync(string url, HostAccount? account, CancellationToken cancellationToken = default)
            => Task.FromResult(GitCredentials.None);
    }
}
