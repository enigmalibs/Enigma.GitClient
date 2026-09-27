using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The integrations on the profiles page: connecting an account to a profile, listing each profile's
/// own, placing the earlier ones that belong to none, and taking them away — one at a time, or with
/// the profile they belong to. What an account can reach is the repositories dialog's, in
/// <see cref="HostRepositoriesDialogTests"/>.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ProfileIntegrationsTests
{
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");
    private static readonly GitIdentity Home = new("Ada", "ada@home.example");

    private readonly HeadlessAvaloniaFixture _fixture;

    public ProfileIntegrationsTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices BuildServices(FakeHostProvider provider)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IRepositoryHostProvider>();
            services.AddSingleton<IRepositoryHostProvider>(provider);
        });

    /// <summary>
    /// The profiles page over a store holding the given profiles, shown once.
    /// </summary>
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

    private static async Task ConnectAsync(
        TestServices services,
        ProfilesPageViewModel page,
        string profile = "Work",
        string instance = "https://github.com",
        string displayName = "")
    {
        AddHostAccountDialogViewModel dialog = new(services.Get<IHostProviderRegistry>().Providers)
        {
            InstanceUrl = instance,
            Token = "ghp_token",
            DisplayName = displayName,
        };

        await page.ConnectAsync(Profile(page, profile), dialog);
    }

    /// <summary>
    /// Stores an account directly, as an earlier version of the application — or another window —
    /// would have left it.
    /// </summary>
    private static async Task<HostAccount> StoredAsync(TestServices services, string name, string? profileId)
    {
        HostAccount account = HostAccount.Create(HostKind.GitHub, new Uri("https://github.com"), name, name, profileId);
        await services.Get<IHostAccountService>().AddAsync(account, new SecretString("token-" + name));
        return account;
    }

    // ---------------------------------------------------------------- the dialog

    [Fact]
    public void TheDialogStartsOnThePublicInstanceAndNeedsAToken()
    {
        _fixture.Run(() =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            AddHostAccountDialogViewModel dialog = new(services.Get<IHostProviderRegistry>().Providers);

            Assert.Equal("https://github.com", dialog.InstanceUrl);
            Assert.Contains("repo", dialog.ScopeHint, StringComparison.Ordinal);
            Assert.Contains("No issue or pull-request scope", dialog.ScopeHint, StringComparison.Ordinal);
            Assert.False(dialog.IsValid);

            dialog.Token = "ghp_token";

            Assert.True(dialog.IsValid);
        });
    }

    [Fact]
    public void TheDialogAcceptsAHostNameAndAssumesHttps()
    {
        _fixture.Run(() =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            AddHostAccountDialogViewModel dialog = new(services.Get<IHostProviderRegistry>().Providers)
            {
                InstanceUrl = "github.example.com",
                Token = "ghp_token",
            };

            Assert.True(dialog.IsValid);
            Assert.Equal(new Uri("https://github.example.com"), dialog.ToAccount().BaseUri);
        });
    }

    [Fact]
    public void TheDialogRefusesSomethingThatIsNotAnAddress()
    {
        _fixture.Run(() =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            AddHostAccountDialogViewModel dialog = new(services.Get<IHostProviderRegistry>().Providers)
            {
                InstanceUrl = "not a url at all",
                Token = "ghp_token",
            };

            Assert.False(dialog.IsValid);
            Assert.True(dialog.HasValidationMessage);
        });
    }

    [Fact]
    public void TheDialogOffersEveryHostTheBuildCanTalkTo()
    {
        _fixture.Run(() =>
        {
            // The real registry this time: what the application ships with, not a double.
            using TestServices services = TestServices.Build();

            IReadOnlyList<IRepositoryHostProvider> providers = services.Get<IHostProviderRegistry>().Providers;

            Assert.Equal(3, providers.Count);
            Assert.Contains(providers, provider => provider.Kind == HostKind.GitHub);
            Assert.Contains(providers, provider => provider.Kind == HostKind.GitLab);
            Assert.Contains(providers, provider => provider.Kind == HostKind.AzureDevOps);

            AddHostAccountDialogViewModel dialog = new(providers);

            Assert.True(dialog.HasChoice);

            // Choosing a host moves the instance and the scope hint with it: the page knows nothing
            // about any particular one.
            foreach (IRepositoryHostProvider provider in providers)
            {
                dialog.SelectedProvider = provider;

                Assert.Equal(provider.DefaultBaseUri.ToString().TrimEnd('/'), dialog.InstanceUrl);
                Assert.Equal(provider.TokenScopeHint, dialog.ScopeHint);
                Assert.Contains(provider.DisplayName, dialog.TokenLabel, StringComparison.Ordinal);
            }
        });
    }

    // ---------------------------------------------------------------- connecting

    [Fact]
    public void ConnectingAsksTheHostWhoTheTokenBelongsToAndStoresTheAccountUnderTheProfile()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new() { Identity = new HostIdentity("octocat", "The Octocat") };
            using TestServices services = BuildServices(provider);

            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work), ("Home", Home));

            Assert.False(Profile(page, "Work").HasIntegrations);

            await ConnectAsync(services, page, "Work");

            HostAccountRowViewModel row = Assert.Single(Profile(page, "Work").Integrations);

            Assert.Equal("The Octocat", row.DisplayName);
            Assert.Equal("octocat", row.UserName);
            Assert.Equal("github.com · octocat", row.Details);
            Assert.Equal("GitHub", row.HostName);
            Assert.False(row.HasMoveTargets);

            // Only the profile it was connected to has it.
            Assert.Empty(Profile(page, "Home").Integrations);
            Assert.False(page.HasEarlierIntegrations);

            // The token was handed to the host to check before anything was stored.
            Assert.Equal("ghp_token", Assert.Single(provider.TokensSeen).Reveal());
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Connected to GitHub");

            // And it is in the store, under the account's own key and the profile's id.
            HostAccount stored = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            Assert.Equal(Profile(page, "Work").Profile.Id, stored.ProfileId);
            Assert.Equal("ghp_token", (await services.Get<IHostAccountService>().GetTokenAsync(stored))!.Reveal());
        });
    }

    [Fact]
    public void TheConnectDialogNamesTheProfileItConnectsTo()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            services.Dialogs.Result = DialogResult.Close;

            await Profile(page, "Work").ConnectCommand.ExecuteAsync(Profile(page, "Work"));

            Assert.Equal("Connect an account to Work", services.Dialogs.Last!.Title);
            Assert.IsType<AddHostAccountDialogView>(services.Dialogs.Last.Content);

            // Cancelled: nothing asked of the host, nothing stored.
            Assert.Empty(provider.TokensSeen);
            Assert.Empty(await services.Get<IHostAccountService>().GetAllAsync());
        });
    }

    [Fact]
    public void ARefusedTokenIsNeverStored()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new()
            {
                ValidationFailure = new HostAuthenticationException("GitHub rejected the token."),
            };

            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page);

            Assert.Empty(Profile(page, "Work").Integrations);
            Assert.Empty(await services.Get<ITokenStore>().ListKeysAsync());
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "GitHub refused the token");
        });
    }

    [Fact]
    public void ARateLimitedConnectionSaysWhenItWillLift()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new()
            {
                ValidationFailure = new HostRateLimitException(
                    "GitHub is rate-limiting this account.",
                    DateTimeOffset.UtcNow.AddMinutes(20)),
            };

            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown, shown => shown.Title == "Rate limited");

            Assert.Contains("minutes", note.Message, StringComparison.Ordinal);
            Assert.Empty(Profile(page, "Work").Integrations);
        });
    }

    [Fact]
    public void AnAccountsOwnNameWinsOverTheOneTheHostGives()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));

            await ConnectAsync(services, page, displayName: "Work GitHub");

            Assert.Equal("Work GitHub", Assert.Single(Profile(page, "Work").Integrations).DisplayName);
        });
    }

    // ---------------------------------------------------------------- listing and placing

    [Fact]
    public void EachProfileListsItsOwnIntegrationsAndTheRestAreEarlier()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await store.SaveAsync(IdentityProfile.Create("Work", Work));
            IdentityProfile home = await store.SaveAsync(IdentityProfile.Create("Home", Home));

            await StoredAsync(services, "work-account", work.Id);
            await StoredAsync(services, "home-account", home.Id);
            await StoredAsync(services, "earlier-account", null);
            await StoredAsync(services, "orphan-account", "a-profile-deleted-elsewhere");

            ProfilesPageViewModel page = await PageAsync(services);

            Assert.Equal("work-account", Assert.Single(Profile(page, "Work").Integrations).DisplayName);
            Assert.Equal("home-account", Assert.Single(Profile(page, "Home").Integrations).DisplayName);

            // Nothing guesses whose an account is: one without a profile, and one whose profile is
            // gone, wait for the user to place them.
            Assert.True(page.HasEarlierIntegrations);
            Assert.Equal(["earlier-account", "orphan-account"], page.EarlierIntegrations.Select(row => row.DisplayName));

            HostAccountRowViewModel earlier = page.EarlierIntegrations[0];
            Assert.True(earlier.HasMoveTargets);
            Assert.Equal(["Work", "Home"], earlier.MoveTargets.Select(target => target.Header));
        });
    }

    [Fact]
    public void MovingAnEarlierIntegrationPutsItUnderTheProfile()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            HostAccount earlier = await StoredAsync(services, "earlier-account", null);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work), ("Home", Home));

            AccountMoveTargetViewModel toHome = page.EarlierIntegrations.Single().MoveTargets.Single(target => target.Header == "Home");

            await toHome.Command.ExecuteAsync(toHome);

            Assert.False(page.HasEarlierIntegrations);
            Assert.Equal("earlier-account", Assert.Single(Profile(page, "Home").Integrations).DisplayName);
            Assert.Empty(Profile(page, "Work").Integrations);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "earlier-account belongs to Home");

            // Moved, not reconnected: the same account, and the same token.
            HostAccount stored = Assert.Single(await services.Get<IHostAccountService>().GetAllAsync());
            Assert.Equal(earlier.Id, stored.Id);
            Assert.Equal(Profile(page, "Home").Profile.Id, stored.ProfileId);
            Assert.Equal("token-earlier-account", (await services.Get<IHostAccountService>().GetTokenAsync(stored))!.Reveal());
        });
    }

    [Fact]
    public void WithoutAProfileAnEarlierIntegrationHasNowhereToGo()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            await StoredAsync(services, "earlier-account", null);
            ProfilesPageViewModel page = await PageAsync(services);

            Assert.False(page.HasProfiles);
            HostAccountRowViewModel earlier = Assert.Single(page.EarlierIntegrations);
            Assert.False(earlier.HasMoveTargets);
        });
    }

    [Fact]
    public void AnAddedProfileIsOfferedToTheEarlierIntegrations()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            services.Identity.Global = Work;

            await StoredAsync(services, "earlier-account", null);
            ProfilesPageViewModel page = await PageAsync(services);

            services.Dialogs.Result = DialogResult.Primary;
            services.Dialogs.OnShown = dialog =>
                ((IdentityProfileDialogViewModel)((Control)dialog.Content!).DataContext!).Label = "Work";

            await page.AddProfileCommand.ExecuteAsync(null);

            // Offered, not given: it is still the user's to move.
            HostAccountRowViewModel earlier = Assert.Single(page.EarlierIntegrations);
            Assert.Equal(["Work"], earlier.MoveTargets.Select(target => target.Header));
            Assert.Empty(Profile(page, "Work").Integrations);
        });
    }

    // ---------------------------------------------------------------- browsing

    [Fact]
    public void BrowsingOpensTheRepositoriesDialogForThatAccount()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([]));

            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));
            await ConnectAsync(services, page, displayName: "Work GitHub");

            HostAccountRowViewModel row = Assert.Single(Profile(page, "Work").Integrations);

            await row.BrowseCommand.ExecuteAsync(row);

            HostRepositoriesDialogView view = Assert.IsType<HostRepositoriesDialogView>(services.Dialogs.Last!.Content);
            HostRepositoriesDialogViewModel dialog = Assert.IsType<HostRepositoriesDialogViewModel>(view.DataContext);

            Assert.Equal(row.Account.Id, dialog.Account.Id);
            Assert.Single(provider.Queries);
        });
    }

    // ---------------------------------------------------------------- removing

    [Fact]
    public void DisconnectingAsksFirstAndThenTakesTheTokenWithIt()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));
            await ConnectAsync(services, page);

            HostAccountRowViewModel row = Assert.Single(Profile(page, "Work").Integrations);

            services.Dialogs.Script(DialogResult.Close, DialogResult.Primary);

            await row.RemoveCommand.ExecuteAsync(row);

            Assert.Single(Profile(page, "Work").Integrations);
            Assert.Equal("Disconnect this account", services.Dialogs.Last!.Title);

            await row.RemoveCommand.ExecuteAsync(row);

            Assert.Empty(Profile(page, "Work").Integrations);
            Assert.Empty(await services.Get<ITokenStore>().ListKeysAsync());
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Account disconnected");
        });
    }

    [Fact]
    public void DeletingAProfileDisconnectsItsIntegrationsAndNoOneElses()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work), ("Home", Home));

            await ConnectAsync(services, page, "Work", displayName: "Work GitHub");
            await ConnectAsync(services, page, "Home", displayName: "Home GitHub");
            await StoredAsync(services, "earlier-account", null);

            services.Dialogs.Result = DialogResult.Primary;

            await page.RemoveProfileCommand.ExecuteAsync(Profile(page, "Work"));

            // The confirmation says what goes with the profile.
            Assert.Contains(
                "Its integration, Work GitHub, is disconnected and its token deleted.",
                (string)services.Dialogs.Last!.Content!,
                StringComparison.Ordinal);

            Assert.Equal(["Home"], page.Profiles.Select(row => row.Label));
            Assert.Equal("Home GitHub", Assert.Single(Profile(page, "Home").Integrations).DisplayName);
            Assert.Equal("earlier-account", Assert.Single(page.EarlierIntegrations).DisplayName);

            IReadOnlyList<HostAccount> left = await services.Get<IHostAccountService>().GetAllAsync();
            Assert.Equal(["Home GitHub", "earlier-account"], left.Select(account => account.DisplayName));
            Assert.Equal(2, (await services.Get<ITokenStore>().ListKeysAsync()).Count);
        });
    }

    [Fact]
    public void IntegrationsComeBackAfterARestart()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work));
            await ConnectAsync(services, page);

            // Reading everything again from the same configuration directory is what a restart does.
            await page.LoadAsync();

            Assert.Single(Profile(page, "Work").Integrations);
            Assert.False(page.HasEarlierIntegrations);
        });
    }

    // ---------------------------------------------------------------- the view

    [Fact]
    public void TheViewShowsEachProfilesIntegrationsAndTheEarlierOnes()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);
            ProfilesPageViewModel page = await PageAsync(services, ("Work", Work), ("Home", Home));

            await ConnectAsync(services, page, "Work", displayName: "Work GitHub");
            await StoredAsync(services, "earlier-account", null);
            await page.LoadIntegrationsAsync();

            ProfilesPageView view = services.Get<ProfilesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1000, Height = 1400 };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                Dispatcher.UIThread.RunJobs();

                // Kept beside the test assembly, as the other page snapshots are, for a reader to look at.
                string directory = System.IO.Path.Combine(AppContext.BaseDirectory, "snapshots");
                System.IO.Directory.CreateDirectory(directory);

                using (Bitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The window produced no rendered frame."))
                {
                    frame.Save(System.IO.Path.Combine(directory, "profiles-integrations.png"), PngBitmapEncoderOptions.Default);
                }

                string[] texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text))
                    .Select(block => block.Text!)];

                Assert.Contains("Work GitHub", texts);
                Assert.Contains("earlier-account", texts);
                Assert.Contains("Local only — no integration, so this profile never pushes.", texts);
                Assert.Contains("Earlier integrations", texts);

                // One Connect per profile; Browse and Disconnect on every integration; Move only on
                // the earlier one.
                Assert.Equal(2, All<Button>(view, "Connect an account to this profile").Count(button => button.IsEffectivelyVisible));
                Assert.Equal(2, All<Button>(view, "Browse repositories").Count(button => button.IsEffectivelyVisible));
                Assert.Equal(2, All<Button>(view, "Disconnect this account").Count(button => button.IsEffectivelyVisible));
                Assert.Equal(1, All<Button>(view, "Move to a profile").Count(button => button.IsEffectivelyVisible));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static IEnumerable<T> All<T>(Control root, string name)
        where T : Control
        => root.GetVisualDescendants()
            .OfType<T>()
            .Where(control => AutomationProperties.GetName(control) == name);
}
