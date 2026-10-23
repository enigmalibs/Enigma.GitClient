using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// What the Profiles page and the repositories dialog say about tokens that cannot be read (BUG-3024):
/// the band with the reset when the whole store is unreadable, each line's own state with
/// <em>Replace token</em>, and a browse that offers the fix instead of throwing.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TokenStateOnProfilesTests
{
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");

    private readonly HeadlessAvaloniaFixture _fixture;

    public TokenStateOnProfilesTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices BuildServices(FakeHostProvider provider)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IRepositoryHostProvider>();
            services.AddSingleton<IRepositoryHostProvider>(provider);
        });

    private static async Task<ProfilesPageViewModel> PageAsync(TestServices services)
    {
        await services.Get<IIdentityProfileStore>().SaveAsync(IdentityProfile.Create("Work", Work));

        ProfilesPageViewModel page = services.Get<ProfilesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static ProfileRowViewModel WorkRow(ProfilesPageViewModel page) => page.Profiles.Single(row => row.Label == "Work");

    private static HostAccountRowViewModel Line(ProfilesPageViewModel page, string userName)
        => WorkRow(page).Integrations.Single(row => row.UserName == userName);

    private static Task ConnectAsync(TestServices services, ProfilesPageViewModel page, FakeHostProvider provider, string userName)
    {
        provider.Identity = new HostIdentity(userName, userName);

        return page.ConnectAsync(
            WorkRow(page),
            new AddHostAccountDialogViewModel(services.Get<IHostProviderRegistry>().Providers) { Token = "ghp_" + userName });
    }

    private static string KeyFile(TestServices services) => Path.Combine(services.ConfigurationRoot, FileTokenStore.KeyFileName);

    private static string TokenFile(TestServices services) => Path.Combine(services.ConfigurationRoot, FileTokenStore.FileName);

    private static void BreakTheKey(TestServices services) => File.WriteAllBytes(KeyFile(services), [1, 2, 3]);

    private static void DamageTheEntryOf(TestServices services, HostAccount account)
    {
        JsonNode document = JsonNode.Parse(File.ReadAllText(TokenFile(services)))!;
        JsonNode entry = document["entries"]![account.TokenKey]!;
        byte[] tag = Convert.FromBase64String(entry["tag"]!.GetValue<string>());
        tag[0] ^= 0xFF;
        entry["tag"] = Convert.ToBase64String(tag);
        File.WriteAllText(TokenFile(services), document.ToJsonString());
    }

    private static async Task<HostAccount> StoredAsync(TestServices services, string userName)
        => (await services.Get<IHostAccountService>().GetAllAsync()).Single(account => account.UserName == userName);

    // ---------------------------------------------------------------- the band

    [Fact]
    public void AHealthyStore_ShowsNoBand_AndNoProblemOnALine()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);

            await ConnectAsync(services, page, provider, "octocat");

            Assert.False(page.IsTokenStoreUnreadable);
            Assert.Equal(HostAccountTokenState.Stored, Line(page, "octocat").TokenState);
            Assert.False(Line(page, "octocat").HasTokenProblem);
        });
    }

    [Fact]
    public void AStoreThatCannotBeRead_ShowsTheBand_AndItsResetLeavesEachLineWithoutAToken()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");

            BreakTheKey(services);
            await page.LoadIntegrationsAsync();

            // Said before anything fails, with what fixes it.
            Assert.True(page.IsTokenStoreUnreadable);
            Assert.Contains("Reset the stored tokens", page.TokenStoreProblem, StringComparison.Ordinal);
            Assert.Equal(HostAccountTokenState.Unreadable, Line(page, "octocat").TokenState);

            services.Dialogs.Script(DialogResult.Primary);
            await page.ResetStoredTokensCommand.ExecuteAsync(null);

            Assert.Equal(TokenRecovery.ResetLabel, Assert.Single(services.Dialogs.Shown).Title);
            Assert.False(page.IsTokenStoreUnreadable);
            Assert.False(File.Exists(KeyFile(services)));

            HostAccountRowViewModel line = Line(page, "octocat");
            Assert.Equal(HostAccountTokenState.Missing, line.TokenState);
            Assert.Contains("No token stored", line.TokenProblem, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheBandsReset_Declined_ChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");
            BreakTheKey(services);
            await page.LoadIntegrationsAsync();

            services.Dialogs.Script(DialogResult.Close);
            await page.ResetStoredTokensCommand.ExecuteAsync(null);

            Assert.True(page.IsTokenStoreUnreadable);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(KeyFile(services)));
        });
    }

    [Fact]
    public void OneDamagedEntry_IsThatLinesProblem_NotTheStores()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");
            await ConnectAsync(services, page, provider, "hubot");

            DamageTheEntryOf(services, await StoredAsync(services, "hubot"));
            await page.LoadIntegrationsAsync();

            Assert.False(page.IsTokenStoreUnreadable);
            Assert.Equal(HostAccountTokenState.Stored, Line(page, "octocat").TokenState);
            Assert.Equal(HostAccountTokenState.Unreadable, Line(page, "hubot").TokenState);
            Assert.Contains("cannot be read", Line(page, "hubot").TokenProblem, StringComparison.Ordinal);
        });
    }

    // ---------------------------------------------------------------- replace token

    [Fact]
    public void ReplaceToken_AsksForATokenForThatAccount_AndStoresItOnTheSameLine()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");
            HostAccount before = await StoredAsync(services, "octocat");
            DamageTheEntryOf(services, before);
            await page.LoadIntegrationsAsync();

            AddHostAccountDialogViewModel? asked = null;
            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is AddHostAccountDialogView { DataContext: AddHostAccountDialogViewModel model })
                {
                    asked = model;
                    model.Token = "ghp_replaced";
                }
            };
            services.Dialogs.Script(DialogResult.Primary);

            await Line(page, "octocat").ReplaceTokenCommand.ExecuteAsync(Line(page, "octocat"));

            // The account's own instance, which the dialog does not let change.
            Assert.NotNull(asked);
            Assert.False(asked!.CanChooseInstance);
            Assert.Equal("https://github.com", asked.InstanceUrl);
            Assert.Equal("Replace the token of octocat", services.Dialogs.Shown[0].Title);

            HostAccount after = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            Assert.Equal(before.Id, after.Id);
            Assert.Equal("ghp_replaced", (await services.Get<IHostAccountService>().GetTokenAsync(after))!.Reveal());
            Assert.Equal(HostAccountTokenState.Stored, Line(page, "octocat").TokenState);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Token replaced for GitHub");
        });
    }

    [Fact]
    public void ReplaceToken_InAStoreThatCannotBeRead_OffersTheReset_ThenStoresTheToken()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");
            BreakTheKey(services);
            await page.LoadIntegrationsAsync();

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is AddHostAccountDialogView { DataContext: AddHostAccountDialogViewModel model })
                {
                    model.Token = "ghp_after_reset";
                }
            };
            services.Dialogs.Script(DialogResult.Primary, DialogResult.Primary);

            await Line(page, "octocat").ReplaceTokenCommand.ExecuteAsync(Line(page, "octocat"));

            Assert.Equal(TokenRecovery.ResetLabel, services.Dialogs.Shown[1].Title);
            HostAccount after = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            Assert.Equal("ghp_after_reset", (await services.Get<IHostAccountService>().GetTokenAsync(after))!.Reveal());
            Assert.False(page.IsTokenStoreUnreadable);
        });
    }

    // ---------------------------------------------------------------- browsing

    [Fact]
    public void BrowsingWithAStoreThatCannotBeRead_ClosesTheDialog_ThenOffersTheReset()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");
            HostAccount account = await StoredAsync(services, "octocat");
            BreakTheKey(services);

            // The dialog's own answer, then the reset's.
            services.Dialogs.Script(DialogResult.None, DialogResult.Primary);

            HostRepository? picked = await services.Get<IHostRepositoryBrowser>().BrowseAsync(account);

            Assert.Null(picked);
            Assert.Empty(provider.Queries);
            Assert.Equal(1, services.Dialogs.Hidden);
            Assert.Equal(2, services.Dialogs.Shown.Count);
            Assert.Equal(TokenRecovery.ResetLabel, services.Dialogs.Shown[1].Title);
            Assert.False(File.Exists(KeyFile(services)));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "The stored token cannot be read");
        });
    }

    [Fact]
    public void BrowsingWithOneDamagedEntry_SaysWhatFixesIt_InTheDialog_AndOffersNoReset()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services);
            await ConnectAsync(services, page, provider, "octocat");
            HostAccount account = await StoredAsync(services, "octocat");
            DamageTheEntryOf(services, account);

            HostRepository? picked = await services.Get<IHostRepositoryBrowser>().BrowseAsync(account);

            Assert.Null(picked);
            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            HostRepositoriesDialogViewModel model = Assert.IsType<HostRepositoriesDialogViewModel>(Assert.IsType<HostRepositoriesDialogView>(dialog.Content).DataContext);

            Assert.Equal(TokenProtectionReason.EntryUnreadable, model.TokenFailure?.Reason);
            Assert.Contains("Replace its token, or disconnect it", model.Summary, StringComparison.Ordinal);
            Assert.Equal(0, services.Dialogs.Hidden);
            Assert.True(File.Exists(KeyFile(services)));
        });
    }
}
