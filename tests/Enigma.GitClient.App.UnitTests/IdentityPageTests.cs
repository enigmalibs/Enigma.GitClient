using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The identity page: where it is reached from, the global name and email it reads from git and
/// writes back, and the profiles that switch them. Every test runs against the in-memory identity,
/// never the developer's own.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class IdentityPageTests
{
    private static readonly GitIdentity Ada = new("Ada Lovelace", "ada@example.com");
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");
    private static readonly GitIdentity Home = new("Ada", "ada@home.example");

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

    // ---------------------------------------------------------------- profiles

    [Fact]
    public void AddingAProfileStartsFromTheGlobalIdentityAndMarksItCurrent()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.False(page.HasProfiles);
            Assert.Null(page.CurrentProfile);

            IdentityProfileDialogViewModel? seen = null;
            services.Dialogs.Result = DialogResult.Primary;
            services.Dialogs.OnShown = dialog =>
            {
                seen = DialogModel(dialog);
                seen.Label = "Personal";
            };

            await page.AddProfileCommand.ExecuteAsync(null);

            Assert.NotNull(seen);
            Assert.Equal("Ada Lovelace", seen.Name);
            Assert.Equal("ada@example.com", seen.Email);
            Assert.Equal("Add a profile", services.Dialogs.Last!.Title);

            IdentityProfile stored = Assert.Single(await services.Get<IIdentityProfileStore>().GetAllAsync());
            Assert.Equal("Personal", stored.Label);
            Assert.Equal(Ada, stored.Identity);

            IdentityProfileRowViewModel row = Assert.Single(page.Profiles);
            Assert.True(row.IsCurrent);
            Assert.Equal(stored, page.CurrentProfile);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Profile added");

            // Adding a profile never writes git's configuration.
            Assert.Equal(0, services.Identity.GlobalWrites);
        });
    }

    [Fact]
    public void TheDialogCanOnlyBeConfirmedWithUsableFields()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            List<bool> enabled = [];
            services.Dialogs.Result = DialogResult.Primary;
            services.Dialogs.OnShown = dialog =>
            {
                IdentityProfileDialogViewModel model = DialogModel(dialog);

                // No label yet.
                enabled.Add(dialog.IsPrimaryButtonEnabled);
                Assert.False(model.HasValidationMessage);

                model.Label = "Work";
                enabled.Add(dialog.IsPrimaryButtonEnabled);

                model.Email = "nope";
                enabled.Add(dialog.IsPrimaryButtonEnabled);
                Assert.Equal("An email needs something on both sides of an @.", model.ValidationMessage);
            };

            await page.AddProfileCommand.ExecuteAsync(null);

            Assert.Equal([false, true, false], enabled);

            // Confirmed while invalid: nothing is stored.
            Assert.Empty(await services.Get<IIdentityProfileStore>().GetAllAsync());
        });
    }

    [Fact]
    public void ACancelledDialogAddsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            services.Dialogs.Result = DialogResult.None;
            services.Dialogs.OnShown = dialog => DialogModel(dialog).Label = "Work";

            await page.AddProfileCommand.ExecuteAsync(null);

            Assert.Empty(await services.Get<IIdentityProfileStore>().GetAllAsync());
            Assert.Empty(page.Profiles);
        });
    }

    [Fact]
    public void EditingAProfileKeepsItsPlaceAndItsIdentifier()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await store.SaveAsync(IdentityProfile.Create("Work", Work));
            await store.SaveAsync(IdentityProfile.Create("Home", Home));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            services.Dialogs.Result = DialogResult.Primary;
            services.Dialogs.OnShown = dialog =>
            {
                IdentityProfileDialogViewModel model = DialogModel(dialog);

                Assert.Equal("Work", model.Label);
                Assert.Equal("ada@work.example", model.Email);

                model.Label = "Office";
                model.Email = "ada@office.example";
            };

            await page.EditProfileCommand.ExecuteAsync(page.Profiles[0]);

            Assert.Equal("Edit the profile", services.Dialogs.Last!.Title);
            Assert.Equal(["Office", "Home"], page.Profiles.Select(row => row.Label));
            Assert.Equal(work.Id, page.Profiles[0].Profile.Id);
            Assert.Equal("Ada Lovelace <ada@office.example>", page.Profiles[0].Summary);
            Assert.Equal(2, (await store.GetAllAsync()).Count);
        });
    }

    [Fact]
    public void DeletingAProfileAsksFirstAndLeavesGitAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            await store.SaveAsync(IdentityProfile.Create("Work", Work));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            services.Dialogs.Script(DialogResult.Close, DialogResult.Primary);

            await page.RemoveProfileCommand.ExecuteAsync(page.Profiles[0]);

            Assert.Equal("Delete this profile", services.Dialogs.Last!.Title);
            Assert.Single(await store.GetAllAsync());
            Assert.Single(page.Profiles);

            await page.RemoveProfileCommand.ExecuteAsync(page.Profiles[0]);

            Assert.Empty(await store.GetAllAsync());
            Assert.Empty(page.Profiles);
            Assert.Null(page.CurrentProfile);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Profile deleted");

            // The identity it described is still git's.
            Assert.Equal(Work, services.Identity.Global);
            Assert.Equal(0, services.Identity.GlobalWrites);
        });
    }

    [Fact]
    public void UsingAProfileMakesItTheGlobalIdentityInOneClick()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            await store.SaveAsync(IdentityProfile.Create("Work", Work));
            await store.SaveAsync(IdentityProfile.Create("Home", Home));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.True(page.Profiles[0].IsCurrent);
            Assert.False(page.Profiles[1].IsCurrent);
            Assert.Equal("Work", page.CurrentProfile?.Label);

            await page.UseProfileCommand.ExecuteAsync(page.Profiles[1]);

            Assert.Equal(Home, services.Identity.Global);
            Assert.Equal("Ada", page.GlobalName);
            Assert.Equal("ada@home.example", page.GlobalEmail);
            Assert.False(page.IsGlobalChanged);

            Assert.False(page.Profiles[0].IsCurrent);
            Assert.True(page.Profiles[1].IsCurrent);
            Assert.Equal("Home", page.CurrentProfile?.Label);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Using Home", note.Title);
            Assert.Contains("Ada <ada@home.example>", note.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheCurrentProfileFollowsAnIdentitySavedByHand()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;
            await services.Get<IIdentityProfileStore>().SaveAsync(IdentityProfile.Create("Work", Work));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Null(page.CurrentProfile);

            // The emails' case does not matter; the names' does.
            page.GlobalEmail = "ADA@work.example";
            await page.SaveGlobalCommand.ExecuteAsync(null);

            Assert.Equal("Work", page.CurrentProfile?.Label);
            Assert.True(page.Profiles[0].IsCurrent);
        });
    }

    [Fact]
    public void AFailedUseIsReportedAndTheCurrentProfileStays()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            await store.SaveAsync(IdentityProfile.Create("Work", Work));
            await store.SaveAsync(IdentityProfile.Create("Home", Home));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            services.Identity.Failure = FakeGitIdentityService.LockFailure();

            await page.UseProfileCommand.ExecuteAsync(page.Profiles[1]);

            Assert.Equal("Could not save the git identity", Assert.Single(services.InfoBar.Shown).Title);
            Assert.Equal("Work", page.CurrentProfile?.Label);
            Assert.Equal(Work, page.GlobalIdentity);
        });
    }

    [Fact]
    public void AProfileFileThatCannotBeReadOrWrittenIsReported()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(configure: collection =>
            {
                collection.RemoveAll<IIdentityProfileStore>();
                collection.AddSingleton<IIdentityProfileStore, FailingProfileStore>();
            });

            services.Identity.Global = Ada;

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Equal("Could not read the identity profiles", Assert.Single(services.InfoBar.Shown).Title);

            // The global identity was still read.
            Assert.Equal(Ada, page.GlobalIdentity);

            services.Dialogs.Result = DialogResult.Primary;
            services.Dialogs.OnShown = dialog => DialogModel(dialog).Label = "Work";

            await page.AddProfileCommand.ExecuteAsync(null);

            Assert.Equal("Could not save the profile", services.InfoBar.Last!.Title);
            Assert.Equal("The disk is full.", services.InfoBar.Last.Message);
        });
    }

    [Fact]
    public void TheDialogSaysNothingUntilSomethingIsTypedAndKeepsAnEditedProfilesIdentifier()
    {
        IdentityProfileDialogViewModel fresh = new(string.Empty, Ada);

        Assert.False(fresh.IsValid);
        Assert.False(fresh.HasValidationMessage);
        Assert.Throws<InvalidOperationException>(() => fresh.ToProfile(existing: null));

        fresh.Label = " Work ";

        Assert.True(fresh.IsValid);
        IdentityProfile created = fresh.ToProfile(existing: null);
        Assert.Equal("Work", created.Label);
        Assert.Equal(Ada, created.Identity);

        IdentityProfileDialogViewModel editing = new("Work", Ada) { Email = "ada@office.example" };
        IdentityProfile edited = editing.ToProfile(created);

        Assert.Equal(created.Id, edited.Id);
        Assert.Equal("ada@office.example", edited.Email);
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

    [Fact]
    public void TheViewMarksTheCurrentProfileAndOffersUseOnTheOthers()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            await store.SaveAsync(IdentityProfile.Create("Work", Work));
            await store.SaveAsync(IdentityProfile.Create("Home", Home));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            IdentityPageView view = services.Get<IdentityPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1000, Height = 900 };
            window.Show();

            try
            {
                Button[] use = [.. All<Button>(view, "Use this profile")];

                Assert.Equal(2, use.Length);
                Assert.False(use[0].IsVisible);
                Assert.True(use[1].IsVisible);

                TextBlock[] pills = [.. view.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Text == "Current")];
                Assert.Equal(2, pills.Length);
                Assert.True(pills[0].IsEffectivelyVisible);
                Assert.False(pills[1].IsEffectivelyVisible);

                Assert.Equal(2, All<Button>(view, "Edit this profile").Count());
                Assert.Equal(2, All<Button>(view, "Delete this profile").Count());
                Assert.True(Named<Button>(view, "Add a profile").IsEffectivelyEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static IdentityProfileDialogViewModel DialogModel(ContentDialog dialog)
        => Assert.IsType<IdentityProfileDialogViewModel>(Assert.IsType<IdentityProfileDialogView>(dialog.Content).DataContext);

    private static IEnumerable<T> All<T>(Control root, string name)
        where T : Control
        => root.GetVisualDescendants()
            .OfType<T>()
            .Where(control => AutomationProperties.GetName(control) == name);

    private static T Named<T>(Control root, string name)
        where T : Control
        => root.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == name);
}

/// <summary>
/// A profile store whose file can be neither read nor written.
/// </summary>
internal sealed class FailingProfileStore : IIdentityProfileStore
{
    public Task<IReadOnlyList<IdentityProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        => throw new UnauthorizedAccessException("Access to the profiles is denied.");

    public Task<IdentityProfile> SaveAsync(IdentityProfile profile, CancellationToken cancellationToken = default)
        => throw new IOException("The disk is full.");

    public Task<bool> RemoveAsync(string profileId, CancellationToken cancellationToken = default)
        => throw new IOException("The disk is full.");
}
