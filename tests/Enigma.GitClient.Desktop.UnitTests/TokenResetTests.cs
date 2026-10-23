using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Stored tokens this account can no longer read (BUG-3024): connecting offers the reset, stores the
/// token just typed once it is confirmed, and an account connected again replaces its own token instead
/// of leaving a dead line beside the new one.
/// </summary>
/// <remarks>
/// On Linux the data key file holds the key itself, so a key file of three bytes is what a Windows
/// password reset makes of DPAPI's: a key that cannot be opened.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class TokenResetTests
{
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");
    private static readonly GitIdentity Home = new("Ada", "ada@home.example");

    private readonly HeadlessAvaloniaFixture _fixture;

    public TokenResetTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices BuildServices(FakeHostProvider provider)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IRepositoryHostProvider>();
            services.AddSingleton<IRepositoryHostProvider>(provider);
        });

    private static async Task<ProfilesPageViewModel> PageAsync(TestServices services, params (string Label, GitIdentity Identity)[] profiles)
    {
        IIdentityProfileStore store = services.Get<IIdentityProfileStore>();

        foreach ((string label, GitIdentity identity) in profiles)
        {
            await store.SaveAsync(IdentityProfile.Create(label, identity));
        }

        ProfilesPageViewModel page = services.Get<ProfilesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static ProfileRowViewModel Profile(ProfilesPageViewModel page, string label)
        => page.Profiles.Single(row => row.Label == label);

    private static Task ConnectAsync(TestServices services, ProfilesPageViewModel page, string profile = "Work", string token = "ghp_token", string displayName = "")
        => page.ConnectAsync(
            Profile(page, profile),
            new AddHostAccountDialogViewModel(services.Get<IHostProviderRegistry>().Providers)
            {
                InstanceUrl = "https://github.com",
                Token = token,
                DisplayName = displayName,
            });

    private static string KeyFile(TestServices services) => Path.Combine(services.ConfigurationRoot, FileTokenStore.KeyFileName);

    /// <summary>Leaves the store as a Windows password reset leaves it: a key this account cannot open.</summary>
    private static void BreakTheKey(TestServices services) => File.WriteAllBytes(KeyFile(services), [1, 2, 3]);

    // ---------------------------------------------------------------- connecting to an unreadable store

    [Fact]
    public void ConnectingAgain_AfterThePasswordReset_OffersTheReset_ThenStoresTheTypedToken()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new() { Identity = new HostIdentity("octocat", "The Octocat") };
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page, token: "ghp_before");
            HostAccount before = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            BreakTheKey(services);

            services.Dialogs.Script(DialogResult.Primary);

            await ConnectAsync(services, page, token: "ghp_after");

            // One question, the reset's, saying what it deletes, what it costs and what it leaves alone.
            ContentDialog question = Assert.Single(services.Dialogs.Shown);
            Assert.Equal(TokenRecovery.ResetLabel, question.Title);
            Assert.Equal(TokenRecovery.ResetLabel, question.PrimaryButtonText);
            string text = Assert.IsType<string>(question.Content);
            Assert.Contains("Every stored token is deleted", text, StringComparison.Ordinal);
            Assert.Contains("needs its token again", text, StringComparison.Ordinal);
            Assert.Contains("Nothing on the host is touched", text, StringComparison.Ordinal);

            // The token typed was stored without being asked for again, on the account already listed.
            HostAccount after = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            Assert.Equal(before.Id, after.Id);
            Assert.Equal("ghp_after", (await services.Get<IHostAccountService>().GetTokenAsync(after))!.Reveal());
            Assert.Equal(2, provider.TokensSeen.Count);
            Assert.Equal(32, File.ReadAllBytes(KeyFile(services)).Length);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Token replaced for GitHub");
            Assert.Single(Profile(page, "Work").Integrations);
        });
    }

    [Fact]
    public void ConnectingToAnUnreadableStore_WhenTheResetIsDeclined_StoresNothing_AndSaysWhatFixesIt()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page, token: "ghp_before");
            BreakTheKey(services);
            services.Dialogs.Script(DialogResult.Close);

            await ConnectAsync(services, page, token: "ghp_after");

            Assert.Single(services.Dialogs.Shown);

            // Nothing deleted, nothing stored: the store is as unreadable as it was.
            Assert.Equal([1, 2, 3], File.ReadAllBytes(KeyFile(services)));
            TokenStoreStatus status = await services.Get<ITokenStore>().CheckAsync();
            Assert.Equal(TokenProtectionReason.KeyUnavailable, status.Failure);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown, shown => shown.Title == "The token was not stored");
            Assert.Equal(InfoBarSeverity.Error, note.Severity);
            Assert.Contains("Reset the stored tokens", note.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ANewAccount_InAnUnreadableStore_IsAddedOnceTheResetIsConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new() { Identity = new HostIdentity("octocat", "The Octocat") };
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page);
            BreakTheKey(services);

            provider.Identity = new HostIdentity("hubot", "Hubot");
            services.Dialogs.Script(DialogResult.Primary);

            await ConnectAsync(services, page, token: "ghp_hubot");

            HostAccount hubot = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync(), account => account.UserName == "hubot");
            Assert.Equal("ghp_hubot", (await services.Get<IHostAccountService>().GetTokenAsync(hubot))!.Reveal());

            // The account listed before stays listed, with no token until it is given one again.
            HostAccount octocat = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync(), account => account.UserName == "octocat");
            Assert.Null(await services.Get<IHostAccountService>().GetTokenAsync(octocat));
        });
    }

    // ---------------------------------------------------------------- no dead duplicate

    [Fact]
    public void ConnectingTheSameAccountAgain_ReplacesItsToken_InsteadOfAddingALine()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new() { Identity = new HostIdentity("octocat", "The Octocat") };
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page, token: "ghp_first", displayName: "Work GitHub");
            HostAccount first = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());

            provider.Identity = new HostIdentity("OctoCat", "The Octocat");
            await ConnectAsync(services, page, token: "ghp_second");

            HostAccount again = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            Assert.Equal(first.Id, again.Id);

            // No name typed this time: the one the reader gave it stays.
            Assert.Equal("Work GitHub", again.DisplayName);
            Assert.Equal("ghp_second", (await services.Get<IHostAccountService>().GetTokenAsync(again))!.Reveal());
            Assert.Single(await services.Get<ITokenStore>().ListKeysAsync());
            Assert.Single(Profile(page, "Work").Integrations);
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void ANameTypedWhenConnectingAgain_RenamesTheAccount()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page, displayName: "Old name");
            await ConnectAsync(services, page, displayName: "New name");

            Assert.Equal("New name", Assert.Single(await services.Get<IHostAccountService>().GetAllAsync()).DisplayName);
        });
    }

    [Fact]
    public void TheSameLogin_ForAnotherProfile_OrAnotherLogin_IsASecondLine()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new() { Identity = new HostIdentity("octocat", "The Octocat") };
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work), ("Home", Home));

            await ConnectAsync(services, page, "Work");
            await ConnectAsync(services, page, "Home");

            provider.Identity = new HostIdentity("hubot", "Hubot");
            await ConnectAsync(services, page, "Work");

            Assert.Equal(3, (await services.Get<IHostAccountService>().GetAllAsync()).Count);
            Assert.Equal(2, Profile(page, "Work").Integrations.Count);
            Assert.Single(Profile(page, "Home").Integrations);
        });
    }

    // ---------------------------------------------------------------- the safety net offers it too

    [Fact]
    public void TheSafetyNet_OffersTheReset_ForStoredTokensThatCannotBeRead()
    {
        _fixture.Run(() =>
        {
            RecordingAppWindows windows = new();
            ScriptedContentDialogService dialogs = new();
            RecordingTokenRecovery recovery = new();
            UnhandledErrorReporter reporter = new(windows, dialogs, new RecordingSystemInterop(), recovery, new ManualTimeProvider(), new RecordingLogger<UnhandledErrorReporter>());
            windows.SetCurrentWindow(new StartWindow());
            dialogs.Script(DialogResult.Primary);

            TokenProtectionException tokens = new(TokenProtectionReason.KeyUnavailable, "The stored tokens belong to a different machine.");
            reporter.Report(new InvalidOperationException("Connecting failed.", tokens), UnhandledErrorSource.Dispatcher);
            Settle();

            ContentDialog dialog = Assert.Single(dialogs.Shown);
            Assert.Equal(TokenRecovery.ResetLabel, dialog.PrimaryButtonText);
            UnhandledErrorViewModel model = (UnhandledErrorViewModel)((UnhandledErrorView)dialog.Content!).DataContext!;
            Assert.True(model.HasRemedy);
            Assert.Equal(1, recovery.Offered);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSafetyNet_OffersNoReset_ForAnythingElse(bool damagedEntry)
    {
        _fixture.Run(() =>
        {
            RecordingAppWindows windows = new();
            ScriptedContentDialogService dialogs = new();
            RecordingTokenRecovery recovery = new();
            UnhandledErrorReporter reporter = new(windows, dialogs, new RecordingSystemInterop(), recovery, new ManualTimeProvider(), new RecordingLogger<UnhandledErrorReporter>());
            windows.SetCurrentWindow(new StartWindow());
            dialogs.Script(DialogResult.Primary);

            Exception error = damagedEntry
                ? new TokenProtectionException(TokenProtectionReason.EntryUnreadable, "One entry.", key: "host:x")
                : new InvalidOperationException("Something else.");

            reporter.Report(error, UnhandledErrorSource.Dispatcher);
            Settle();

            ContentDialog dialog = Assert.Single(dialogs.Shown);
            Assert.Null(dialog.PrimaryButtonText);
            Assert.False(((UnhandledErrorViewModel)((UnhandledErrorView)dialog.Content!).DataContext!).HasRemedy);
            Assert.Equal(0, recovery.Offered);
        });
    }

    private static void Settle()
    {
        for (int pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
