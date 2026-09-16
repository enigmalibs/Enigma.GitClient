using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// A hosting provider that answers from a script, so the page can be driven with no network and no
/// real account.
/// </summary>
internal sealed class FakeHostProvider : IRepositoryHostProvider
{
    private readonly Queue<HostRepositoryPage> _pages = new();

    public HostKind Kind => HostKind.GitHub;

    public string DisplayName => "GitHub";

    public Uri DefaultBaseUri { get; } = new("https://github.com");

    public string TokenScopeHint => "A token with the 'repo' scope. No issue or pull-request scope is ever requested.";

    /// <summary>What validation answers, or the exception it throws instead.</summary>
    public HostIdentity Identity { get; set; } = new("octocat", "The Octocat");

    public Exception? ValidationFailure { get; set; }

    public Exception? ListingFailure { get; set; }

    public List<SecretString> TokensSeen { get; } = [];

    public List<HostRepositoryQuery> Queries { get; } = [];

    public void Returns(params HostRepositoryPage[] pages)
    {
        foreach (HostRepositoryPage page in pages)
        {
            _pages.Enqueue(page);
        }
    }

    public bool MatchesRemote(RemoteUrl remote) => WellKnownHosts.Detect(remote) == HostKind.GitHub;

    public Task<HostIdentity> ValidateCredentialAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default)
    {
        TokensSeen.Add(token);

        return ValidationFailure is not null
            ? Task.FromException<HostIdentity>(ValidationFailure)
            : Task.FromResult(Identity);
    }

    public Task<HostRepositoryPage> ListRepositoriesAsync(
        HostAccount account,
        SecretString token,
        HostRepositoryQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(query);

        return ListingFailure is not null
            ? Task.FromException<HostRepositoryPage>(ListingFailure)
            : Task.FromResult(_pages.Count > 0 ? _pages.Dequeue() : HostRepositoryPage.Empty);
    }

    public string? BuildCommitUrl(RemoteUrl remote, string sha)
        => $"https://github.com/{remote.Path}/commit/{sha}";

    public string? BuildBranchUrl(RemoteUrl remote, string branch)
        => $"https://github.com/{remote.Path}/tree/{branch}";

    public string? BuildFileUrl(RemoteUrl remote, string reference, string path, int? line = null)
        => $"https://github.com/{remote.Path}/blob/{reference}/{path}";
}

/// <summary>
/// Drives the integrations page: connecting an account, listing what it can reach, and taking it
/// away again.
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

    private static HostRepository Repository(
        string fullName,
        bool isPrivate = false,
        string? description = null)
    {
        string name = fullName[(fullName.LastIndexOf('/') + 1)..];

        return new HostRepository(
            fullName,
            name,
            description,
            "main",
            $"https://github.com/{fullName}.git",
            $"git@github.com:{fullName}.git",
            $"https://github.com/{fullName}",
            isPrivate,
            DateTimeOffset.UtcNow.AddDays(-1));
    }

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

    // ---------------------------------------------------------------- listing

    [Fact]
    public void SelectingAnAccountListsWhatItCanReach()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage(
            [
                Repository("octocat/hello-world", description: "My first repository"),
                Repository("contoso/secret-plans", isPrivate: true),
            ]));

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            Assert.Equal(2, page.Repositories.Count);
            Assert.Equal("2 repositories", page.Summary);

            HostRepositoryRowViewModel first = page.Repositories[0];

            Assert.Equal("hello-world", first.Name);
            Assert.Equal("octocat", first.Owner);
            Assert.Equal("My first repository", first.Description);
            Assert.False(first.IsPrivate);
            Assert.True(page.Repositories[1].IsPrivate);
        });
    }

    [Fact]
    public void TheListingFollowsEveryPageTheHostOffers()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(
                new HostRepositoryPage([Repository("o/one")], "cursor-2"),
                new HostRepositoryPage([Repository("o/two")], "cursor-3"),
                new HostRepositoryPage([Repository("o/three")]));

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            Assert.Equal(3, page.Repositories.Count);
            Assert.Equal([null, "cursor-2", "cursor-3"], provider.Queries.Select(query => query.Cursor));
        });
    }

    [Fact]
    public void TheSearchBoxFiltersWhatWasRead()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage(
            [
                Repository("octocat/hello-world"),
                Repository("contoso/secret-plans", description: "Nothing to see"),
            ]));

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            page.Search = "hello";
            Assert.Equal("octocat/hello-world", Assert.Single(page.Repositories).Repository.FullName);

            // The description is searched too, because that is where a repository says what it is.
            page.Search = "nothing to see";
            Assert.Equal("contoso/secret-plans", Assert.Single(page.Repositories).Repository.FullName);

            page.ClearSearchCommand.Execute(null);
            Assert.Equal(2, page.Repositories.Count);
        });
    }

    [Fact]
    public void TheVisibilityFilterIsANewRequestRatherThanALocalFilter()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([Repository("o/one")]));

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            provider.Returns(new HostRepositoryPage([Repository("o/private", isPrivate: true)]));
            page.ShowPrivateCommand.Execute(null);

            Assert.True(page.IsPrivateOnly);
            Assert.Equal(HostVisibility.Private, provider.Queries[^1].Visibility);
            Assert.Equal("o/private", Assert.Single(page.Repositories).Repository.FullName);
        });
    }

    [Fact]
    public void AFailedListingIsReportedAndLeavesTheListEmpty()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new()
            {
                ListingFailure = new HostRequestException("GitHub answered 500.", System.Net.HttpStatusCode.InternalServerError),
            };

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            Assert.Empty(page.Repositories);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Could not list the repositories");
        });
    }

    // ---------------------------------------------------------------- removing and opening

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
    public void OpeningARepositoryHandsItsAddressToTheBrowser()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([Repository("octocat/hello-world")]));

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            await page.OpenRepositoryCommand.ExecuteAsync(page.Repositories[0]);

            Assert.Contains("https://github.com/octocat/hello-world", services.Interop.Opened);
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

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void ThePageActuallyPaintsItsAccountsAndRepositories()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage(
            [
                Repository("octocat/hello-world", description: "My first repository"),
                Repository("contoso/secret-plans", isPrivate: true, description: "Nothing to see here"),
            ]));

            using TestServices services = BuildServices(provider);

            IntegrationsPageViewModel page = services.Get<IntegrationsPageViewModel>();
            await page.OnAppearingAsync();
            await ConnectAsync(services, page);

            IntegrationsPageView view = services.Get<IntegrationsPageView>();
            view.DataContext = page;

            Window window = new() { Width = 1280, Height = 800, Content = view };

            string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "integrations-page-dark.png");

            window.Show();

            int colours = 0;

            try
            {
                for (int attempt = 0; attempt < 20 && colours < 8; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    window.Measure(new Size(window.Width, window.Height));
                    window.Arrange(new Rect(0, 0, window.Width, window.Height));
                    window.UpdateLayout();
                    window.InvalidateVisual();

                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The window produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = CountColours(path);
                }
            }
            finally
            {
                window.Content = null;
                window.Close();
            }

            Assert.True(colours >= 8, $"the frame holds only {colours.ToString(System.Globalization.CultureInfo.InvariantCulture)} distinct colours");
        });
    }

    private static unsafe int CountColours(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap writeable = WriteableBitmap.Decode(stream);
        using ILockedFramebuffer buffer = writeable.Lock();

        HashSet<int> colours = [];

        byte* pixels = (byte*)buffer.Address;

        for (int y = 0; y < buffer.Size.Height; y++)
        {
            byte* row = pixels + (y * buffer.RowBytes);

            for (int x = 0; x < buffer.Size.Width; x++)
            {
                colours.Add(((row[(x * 4) + 2] >> 4) << 8) | ((row[(x * 4) + 1] >> 4) << 4) | (row[(x * 4) + 0] >> 4));
            }
        }

        return colours.Count;
    }

    [Fact]
    public void ARateLimitIsDescribedWithATimeRatherThanTryAgainLater()
    {
        HostRateLimitException soon = new("GitHub is rate-limiting this account.", DateTimeOffset.UtcNow.AddSeconds(30));
        HostRateLimitException later = new("GitHub is rate-limiting this account.", DateTimeOffset.UtcNow.AddMinutes(45));
        HostRateLimitException unknown = new("GitHub is rate-limiting this account.");

        Assert.Contains("within a minute", IntegrationsPageViewModel.Describe(soon), StringComparison.Ordinal);
        Assert.Contains("45 minutes", IntegrationsPageViewModel.Describe(later), StringComparison.Ordinal);
        Assert.Equal(unknown.Message, IntegrationsPageViewModel.Describe(unknown));
    }
}
