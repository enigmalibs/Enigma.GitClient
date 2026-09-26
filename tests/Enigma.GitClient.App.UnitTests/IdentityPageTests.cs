using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The identity page: where it is reached from, and the global name and email it reads from git and
/// writes back. Every test runs against the in-memory identity, never the developer's own.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class IdentityPageTests
{
    private static readonly GitIdentity Ada = new("Ada Lovelace", "ada@example.com");

    private readonly HeadlessAvaloniaFixture _fixture;

    public IdentityPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- where it is

    [Fact]
    public void TheStartWindowOpensTheIdentityPage()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            StartWindowViewModel window = services.Get<StartWindowViewModel>();

            window.StartNavigation.GoTo(StartPage.Identity);

            Assert.Equal(StartPage.Identity, window.StartNavigation.Current);
            Assert.Equal("Identity", window.Navigation.SelectedItem?.Header);
            IdentityPageView page = Assert.IsType<IdentityPageView>(window.Navigation.CurrentPage);
            Assert.Same(services.Get<IdentityPageViewModel>(), page.DataContext);
        });
    }

    [Fact]
    public void TheRepositoryWindowOpensTheSameIdentityPage()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel window = services.Get<MainWindowViewModel>();
            IShellNavigation shell = services.Get<IShellNavigation>();

            shell.GoTo(ShellPage.Identity);

            Assert.Equal(ShellPage.Identity, shell.Current);
            IdentityPageView page = Assert.IsType<IdentityPageView>(window.Navigation.CurrentPage);
            Assert.Same(services.Get<IdentityPageViewModel>(), page.DataContext);
        });
    }

    // ---------------------------------------------------------------- reading

    [Fact]
    public void ThePageShowsTheGlobalIdentityGitHas()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Equal("Ada Lovelace", page.GlobalName);
            Assert.Equal("ada@example.com", page.GlobalEmail);
            Assert.Equal(Ada, page.GlobalIdentity);
            Assert.False(page.IsGlobalUnset);
            Assert.False(page.IsGlobalChanged);
            Assert.False(page.HasGlobalError);
            Assert.Contains("Ada Lovelace <ada@example.com>", page.GlobalSummary, StringComparison.Ordinal);

            // Nothing to save until something changes.
            Assert.False(page.SaveGlobalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AnUnsetIdentityIsSaidAndIsNotAMistake()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.True(page.IsGlobalUnset);
            Assert.Contains("git refuses to commit", page.GlobalSummary, StringComparison.Ordinal);

            // Empty fields for an identity git does not have are not the reader's error.
            Assert.False(page.HasGlobalError);
            Assert.False(page.SaveGlobalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AFailedReadIsReported()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Failure = new GitNotFoundException("git was not found.");

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.LoadAsync();

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not read the git identity", note.Title);
            Assert.Equal(InfoBarSeverity.Error, note.Severity);
            Assert.False(page.IsBusy);
        });
    }

    [Fact]
    public void ComingBackToThePageKeepsWhatWasTypedAndFollowsTheRest()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.GlobalName = "Ada King";

            // Changed in a terminal meanwhile.
            services.Identity.Global = new GitIdentity("Someone Else", "else@example.com");
            await page.OnAppearingAsync();

            Assert.Equal("Ada King", page.GlobalName);
            Assert.Equal(new GitIdentity("Someone Else", "else@example.com"), page.GlobalIdentity);

            // A page nobody typed on follows git entirely.
            using TestServices fresh = TestServices.Build();
            fresh.Identity.Global = Ada;
            IdentityPageViewModel untouched = fresh.Get<IdentityPageViewModel>();
            await untouched.OnAppearingAsync();

            fresh.Identity.Global = new GitIdentity("Someone Else", "else@example.com");
            await untouched.OnAppearingAsync();

            Assert.Equal("Someone Else", untouched.GlobalName);
            Assert.Equal("else@example.com", untouched.GlobalEmail);
        });
    }

    // ---------------------------------------------------------------- saving

    [Fact]
    public void SavingWritesTheTrimmedIdentityToGit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.GlobalName = "  Ada King Lovelace ";
            page.GlobalEmail = " ada@work.example ";

            Assert.True(page.IsGlobalChanged);
            Assert.True(page.SaveGlobalCommand.CanExecute(null));

            await page.SaveGlobalCommand.ExecuteAsync(null);

            Assert.Equal(new GitIdentity("Ada King Lovelace", "ada@work.example"), services.Identity.Global);
            Assert.Equal("Ada King Lovelace", page.GlobalName);
            Assert.Equal("ada@work.example", page.GlobalEmail);
            Assert.False(page.IsGlobalChanged);
            Assert.False(page.SaveGlobalCommand.CanExecute(null));

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Global identity saved", note.Title);
            Assert.Equal(InfoBarSeverity.Success, note.Severity);
        });
    }

    [Fact]
    public void SpacesAloneAreNotAChange()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.GlobalName = " Ada Lovelace  ";

            Assert.False(page.IsGlobalChanged);
            Assert.False(page.SaveGlobalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AnUnusableValueIsSaidAndCannotBeSaved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.GlobalEmail = "ada at example.com";

            Assert.True(page.HasGlobalError);
            Assert.Equal("An email cannot contain spaces.", page.GlobalError);
            Assert.False(page.SaveGlobalCommand.CanExecute(null));

            page.GlobalEmail = string.Empty;
            Assert.Equal("Enter an email.", page.GlobalError);

            page.GlobalEmail = "ada@example.org";
            Assert.False(page.HasGlobalError);
            Assert.True(page.SaveGlobalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AFirstIdentityCanBeSavedOnAMachineWithoutOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.GlobalName = "Ada Lovelace";
            Assert.Equal("Enter an email.", page.GlobalError);

            page.GlobalEmail = "ada@example.com";
            await page.SaveGlobalCommand.ExecuteAsync(null);

            Assert.Equal(Ada, services.Identity.Global);
            Assert.False(page.IsGlobalUnset);
        });
    }

    [Fact]
    public void AFailedSaveIsReportedInGitsWordsAndKeepsWhatWasTyped()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            services.Identity.Failure = FakeGitIdentityService.LockFailure();
            page.GlobalName = "Ada King";

            await page.SaveGlobalCommand.ExecuteAsync(null);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not save the git identity", note.Title);
            Assert.Equal(InfoBarSeverity.Error, note.Severity);
            Assert.StartsWith("error: could not lock config file", note.Message, StringComparison.Ordinal);

            Assert.Equal("Ada King", page.GlobalName);
            Assert.Equal(Ada, page.GlobalIdentity);
            Assert.Equal(0, services.Identity.GlobalWrites);

            // Still one click away from trying again.
            Assert.True(page.SaveGlobalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Describe_UsesGitsOwnWordsAndNeverTheCommandLine()
    {
        GitCommandException silent = new(
            new GitCommand(".", ["config", "--global", "user.name", "Ada"], environment: null, standardInput: null),
            4,
            string.Empty,
            string.Empty);

        Assert.Equal("git stopped with exit code 4.", IdentityPageViewModel.Describe(silent));
        Assert.StartsWith("error:", IdentityPageViewModel.Describe(FakeGitIdentityService.LockFailure()), StringComparison.Ordinal);
        Assert.Equal("Enter a name.", IdentityPageViewModel.Describe(new ArgumentException("Enter a name.", "identity")));
    }

    // ---------------------------------------------------------------- the view

    [Fact]
    public void TheViewBindsTheFieldsAndTheSaveButton()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            IdentityPageView view = services.Get<IdentityPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1000, Height = 800 };
            window.Show();

            try
            {
                TextBox name = Named<TextBox>(view, "Global name");
                TextBox email = Named<TextBox>(view, "Global email");
                Button save = Named<Button>(view, "Save the global identity");

                Assert.Equal("Ada Lovelace", name.Text);
                Assert.Equal("ada@example.com", email.Text);
                Assert.False(save.IsEffectivelyEnabled);

                name.Text = "Ada King";

                Assert.Equal("Ada King", page.GlobalName);
                Assert.True(save.IsEffectivelyEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T Named<T>(Control root, string name)
        where T : Control
        => root.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == name);
}
