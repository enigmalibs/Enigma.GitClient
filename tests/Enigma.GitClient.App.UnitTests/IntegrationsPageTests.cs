using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the integrations page: connecting an account and taking it away again. What an account
/// can reach is the repositories dialog's, in <see cref="HostRepositoriesDialogTests"/>.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class IntegrationsPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public IntegrationsPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices BuildServices(FakeHostProvider provider)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IRepositoryHostProvider>();
            services.AddSingleton<IRepositoryHostProvider>(provider);
        });

    private static async Task<IntegrationsPageViewModel> ConnectAsync(
        TestServices services,
        IntegrationsPageViewModel page,
        string instance = "https://github.com")
    {
        AddHostAccountDialogViewModel dialog = new(services.Get<IHostProviderRegistry>().Providers)
        {
            InstanceUrl = instance,
            Token = "ghp_token",
        };

        await page.ConnectAsync(dialog);

        return page;
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
    public void ConnectingAsksTheHostWhoTheTokenBelongsToAndStoresTheAccount()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new() { Identity = new HostIdentity("octocat", "The Octocat") };
            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();

            Assert.False(page.HasAccounts);

            await ConnectAsync(services, page);

            HostAccountRowViewModel row = Assert.Single(page.Accounts);

            Assert.Equal("The Octocat", row.DisplayName);
            Assert.Equal("octocat", row.UserName);
            Assert.Equal("github.com", row.Host);
            Assert.Equal("GitHub", row.HostName);

            // The token was handed to the host to check before anything was stored.
            Assert.Equal("ghp_token", Assert.Single(provider.TokensSeen).Reveal());
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Connected to GitHub");

            // And it is in the store, under the account's own key.
            SecretString? stored = await services.Get<IHostAccountService>().GetTokenAsync(row.Account);

            Assert.Equal("ghp_token", stored!.Reveal());
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

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();

            await ConnectAsync(services, page);

            Assert.Empty(page.Accounts);
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

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();

            await ConnectAsync(services, page);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown, shown => shown.Title == "Rate limited");

            Assert.Contains("minutes", note.Message, StringComparison.Ordinal);
            Assert.Empty(page.Accounts);
        });
    }

    [Fact]
    public void AnAccountsOwnNameWinsOverTheOneTheHostGives()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();

            AddHostAccountDialogViewModel dialog = new(services.Get<IHostProviderRegistry>().Providers)
            {
                Token = "ghp_token",
                DisplayName = "Work",
            };

            await page.ConnectAsync(dialog);

            Assert.Equal("Work", Assert.Single(page.Accounts).DisplayName);
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

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            HostAccountRowViewModel row = Assert.Single(page.Accounts);

            services.Dialogs.Script(DialogResult.Close, DialogResult.Primary);

            await page.RemoveAccountCommand.ExecuteAsync(row);

            Assert.Single(page.Accounts);
            Assert.Equal("Disconnect this account", services.Dialogs.Last!.Title);

            await page.RemoveAccountCommand.ExecuteAsync(row);

            Assert.Empty(page.Accounts);
            Assert.Empty(await services.Get<ITokenStore>().ListKeysAsync());
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Account disconnected");
        });
    }

    [Fact]
    public void AccountsComeBackAfterARestart()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            // A second page over the same configuration directory is what a restart looks like.
            IntegrationsPageViewModel reopened = services.Get<IntegrationsPageViewModel>();
            await reopened.LoadAccountsAsync();

            Assert.Single(reopened.Accounts);
        });
    }
}
