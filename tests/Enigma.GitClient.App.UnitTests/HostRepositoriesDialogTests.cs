using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the dialog listing what one account can reach — the paged listing, the filter, the
/// visibility, the failures, opening and cloning — and the service that shows it.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HostRepositoriesDialogTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HostRepositoriesDialogTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

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

    /// <summary>
    /// An account whose token is stored, as connecting leaves it.
    /// </summary>
    private static async Task<HostAccount> ConnectedAsync(TestServices services, string? token = "ghp_token")
    {
        HostAccount account = HostAccount.Create(HostKind.GitHub, new Uri("https://github.com"), "octocat", "Work");

        if (token is null)
        {
            await services.Get<IHostAccountService>().UpdateAsync(account);
        }
        else
        {
            await services.Get<IHostAccountService>().AddAsync(account, new SecretString(token));
        }

        return account;
    }

    private static async Task<HostRepositoriesDialogViewModel> DialogAsync(
        TestServices services,
        FakeHostProvider provider,
        string? token = "ghp_token")
        => new(
            await ConnectedAsync(services, token),
            provider,
            services.Get<IHostAccountService>(),
            services.Get<IHostLinkService>(),
            services.InfoBar,
            NullLogger.Instance);

    // ---------------------------------------------------------------- listing

    [Fact]
    public void LoadingListsWhatTheAccountCanReach()
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
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);

            await dialog.LoadAsync();

            Assert.Equal("Work on GitHub", dialog.Title);
            Assert.Equal(2, dialog.Repositories.Count);
            Assert.Equal("2 repositories", dialog.Summary);

            HostRepositoryRowViewModel first = dialog.Repositories[0];

            Assert.Equal("hello-world", first.Name);
            Assert.Equal("octocat", first.Owner);
            Assert.Equal("My first repository", first.Description);
            Assert.False(first.IsPrivate);
            Assert.True(dialog.Repositories[1].IsPrivate);
            Assert.False(dialog.IsBusy);
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
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);

            await dialog.LoadAsync();

            Assert.Equal(3, dialog.Repositories.Count);
            Assert.Equal([null, "cursor-2", "cursor-3"], provider.Queries.Select(query => query.Cursor));
        });
    }

    [Fact]
    public void TheListingStopsAtItsLimitAndSaysThereAreMore()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();

            for (int page = 0; page <= HostRepositoriesDialogViewModel.PageLimit; page++)
            {
                provider.Returns(new HostRepositoryPage([Repository($"o/repo-{page.ToString(System.Globalization.CultureInfo.InvariantCulture)}")], "more"));
            }

            using TestServices services = BuildServices(provider);
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);

            await dialog.LoadAsync();

            Assert.Equal(HostRepositoriesDialogViewModel.PageLimit, provider.Queries.Count);
            Assert.StartsWith("10 repositories, and there are more.", dialog.Summary, StringComparison.Ordinal);
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
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);
            await dialog.LoadAsync();

            dialog.Search = "hello";
            Assert.Equal("octocat/hello-world", Assert.Single(dialog.Repositories).Repository.FullName);

            // The description is searched too, because that is where a repository says what it is.
            dialog.Search = "nothing to see";
            Assert.Equal("contoso/secret-plans", Assert.Single(dialog.Repositories).Repository.FullName);

            dialog.ClearSearchCommand.Execute(null);
            Assert.Equal(2, dialog.Repositories.Count);
            Assert.Single(provider.Queries);
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
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);
            await dialog.LoadAsync();

            provider.Returns(new HostRepositoryPage([Repository("o/private", isPrivate: true)]));
            dialog.ShowPrivateCommand.Execute(null);

            await WaitUntilAsync(() => dialog.IsNotBusy && dialog.HasRepositories);

            Assert.True(dialog.IsPrivateOnly);
            Assert.Equal(HostVisibility.Private, provider.Queries[^1].Visibility);
            Assert.Equal("o/private", Assert.Single(dialog.Repositories).Repository.FullName);
        });
    }

    [Fact]
    public void AFailedListingIsSaidInTheDialogAndOnTheBar()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new()
            {
                ListingFailure = new HostRequestException("GitHub answered 500.", System.Net.HttpStatusCode.InternalServerError),
            };

            using TestServices services = BuildServices(provider);
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);

            await dialog.LoadAsync();

            Assert.Empty(dialog.Repositories);
            Assert.Equal("Could not list the repositories. GitHub answered 500.", dialog.Summary);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not list the repositories", note.Title);
            Assert.Equal(InfoBarSeverity.Error, note.Severity);
        });
    }

    [Fact]
    public void AnAccountWithoutATokenAsksNothingOfTheHost()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();

            using TestServices services = BuildServices(provider);
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider, token: null);

            await dialog.LoadAsync();

            Assert.Empty(provider.Queries);
            Assert.StartsWith("No token for this account.", dialog.Summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ARateLimitIsDescribedWithATimeRatherThanTryAgainLater()
    {
        HostRateLimitException soon = new("GitHub is rate-limiting this account.", DateTimeOffset.UtcNow.AddSeconds(30));
        HostRateLimitException later = new("GitHub is rate-limiting this account.", DateTimeOffset.UtcNow.AddMinutes(45));
        HostRateLimitException unknown = new("GitHub is rate-limiting this account.");

        Assert.Contains("within a minute", HostRepositoriesDialogViewModel.Describe(soon), StringComparison.Ordinal);
        Assert.Contains("45 minutes", HostRepositoriesDialogViewModel.Describe(later), StringComparison.Ordinal);
        Assert.Equal(unknown.Message, HostRepositoriesDialogViewModel.Describe(unknown));
    }

    // ---------------------------------------------------------------- opening and cloning

    [Fact]
    public void OpeningARepositoryHandsItsAddressToTheBrowser()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([Repository("octocat/hello-world")]));

            using TestServices services = BuildServices(provider);
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);
            await dialog.LoadAsync();

            await dialog.OpenCommand.ExecuteAsync(dialog.Repositories[0]);

            Assert.Contains("https://github.com/octocat/hello-world", services.Interop.Opened);
        });
    }

    [Fact]
    public void CloningNamesTheRepositoryAndAsksForTheDialogToClose()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([Repository("octocat/hello-world")]));

            using TestServices services = BuildServices(provider);
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);
            await dialog.LoadAsync();

            int asked = 0;
            dialog.CloneRequested += (_, _) => asked++;

            Assert.Null(dialog.Picked);

            dialog.CloneCommand.Execute(dialog.Repositories[0]);

            Assert.Equal(1, asked);
            Assert.Equal("octocat/hello-world", dialog.Picked?.FullName);
        });
    }

    [Fact]
    public void TheBrowserShowsTheDialogAndReadsTheListIntoIt()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([Repository("octocat/hello-world")]));

            using TestServices services = BuildServices(provider);
            HostAccount account = await ConnectedAsync(services);

            HostRepository? picked = await services.Get<IHostRepositoryBrowser>().BrowseAsync(account);

            // Closed without a pick: nothing to clone.
            Assert.Null(picked);
            Assert.Equal(0, services.Dialogs.Hidden);

            HostRepositoriesDialogView view = Assert.IsType<HostRepositoriesDialogView>(services.Dialogs.Last!.Content);
            HostRepositoriesDialogViewModel model = Assert.IsType<HostRepositoriesDialogViewModel>(view.DataContext);

            Assert.Same(account, model.Account);
            Assert.Equal("Close", services.Dialogs.Last.CloseButtonText);
            Assert.Equal("octocat/hello-world", Assert.Single(model.Repositories).Repository.FullName);
        });
    }

    [Fact]
    public void TheBrowserClosesTheDialogOnCloneAndHandsBackTheRepository()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();
            provider.Returns(new HostRepositoryPage([Repository("octocat/hello-world")]));

            using TestServices services = BuildServices(provider);
            HostAccount account = await ConnectedAsync(services);

            // What the reader does once the dialog is up: press Clone on a row.
            services.Dialogs.OnShown = shown =>
            {
                HostRepositoriesDialogViewModel model = (HostRepositoriesDialogViewModel)((Control)shown.Content!).DataContext!;
                model.CloneCommand.Execute(new HostRepositoryRowViewModel(model, Repository("octocat/hello-world")));
            };

            HostRepository? picked = await services.Get<IHostRepositoryBrowser>().BrowseAsync(account);

            Assert.Equal("octocat/hello-world", picked?.FullName);
            Assert.Equal(1, services.Dialogs.Hidden);
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void TheDialogActuallyPaintsItsRepositories()
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
            HostRepositoriesDialogViewModel dialog = await DialogAsync(services, provider);
            await dialog.LoadAsync();

            HostRepositoriesDialogView view = services.Get<HostRepositoriesDialogView>();
            view.DataContext = dialog;

            Window window = new() { Width = 900, Height = 600, Content = view };

            string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "host-repositories-dialog-dark.png");

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

                IReadOnlyList<string> texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text))
                    .Select(block => block.Text!)];

                Assert.Contains("hello-world", texts);
                Assert.Contains("private", texts);
                Assert.Contains("2 repositories", texts);
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
