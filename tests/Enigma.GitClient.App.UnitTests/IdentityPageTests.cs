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
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The identity page: where it is reached from, the global name and email it reads from git and
/// writes back, the profiles that switch them, and the open repository's own identity. Every test
/// runs against the in-memory identity, never the developer's own.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class IdentityPageTests
{
    private static readonly GitIdentity Ada = new("Ada Lovelace", "ada@example.com");
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");
    private static readonly GitIdentity Home = new("Ada", "ada@home.example");

    /// <summary>A repository that exists only as a handle: the identity behind it is in memory.</summary>
    private static readonly RepositoryHandle WorkRepository = Handle("work");

    /// <summary>A second one, for switching.</summary>
    private static readonly RepositoryHandle HomeRepository = Handle("home");

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

    // ---------------------------------------------------------------- the repository's own identity

    [Fact]
    public void WithoutARepositoryThereIsNoRepositorySection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            await services.Get<IIdentityProfileStore>().SaveAsync(IdentityProfile.Create("Work", Work));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.False(page.IsRepositoryOpen);
            Assert.Equal(string.Empty, page.RepositoryName);
            Assert.False(page.SaveLocalCommand.CanExecute(null));
            Assert.False(page.RemoveLocalCommand.CanExecute(null));

            // A current profile, but nothing to copy it into.
            Assert.True(page.HasCurrentProfile);
            Assert.False(page.CopyFromCurrentProfileCommand.CanExecute(null));
        });
    }

    [Fact]
    public void ThePageReadsTheRepositorysOwnIdentity()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Home;
            services.Identity.SetLocalDirectly(WorkRepository.WorkTreePath, Work);
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.True(page.IsRepositoryOpen);
            Assert.Equal("work", page.RepositoryName);
            Assert.Equal(Work, page.LocalIdentity);
            Assert.Equal("Ada Lovelace", page.LocalName);
            Assert.Equal("ada@work.example", page.LocalEmail);
            Assert.True(page.HasLocalIdentity);
            Assert.False(page.IsLocalChanged);
            Assert.Equal(
                "Commits in work are made as Ada Lovelace <ada@work.example>, whatever the global identity is.",
                page.LocalSummary);
            Assert.True(page.RemoveLocalCommand.CanExecute(null));
            Assert.False(page.SaveLocalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void ARepositoryWithoutAnIdentitySaysItUsesTheGlobalOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.False(page.HasLocalIdentity);
            Assert.False(page.RemoveLocalCommand.CanExecute(null));
            Assert.Contains("there is no global one: git refuses to commit here", page.LocalSummary, StringComparison.Ordinal);

            services.Identity.Global = Home;
            await page.LoadAsync();

            Assert.Equal("work has no identity of its own: its commits use the global one, Ada <ada@home.example>.", page.LocalSummary);
        });
    }

    [Fact]
    public void SavingGivesTheRepositoryItsOwnIdentityAndLeavesTheGlobalOneAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Home;
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.LocalName = " Ada Lovelace ";
            Assert.Equal("Enter an email.", page.LocalError);
            Assert.False(page.SaveLocalCommand.CanExecute(null));

            page.LocalEmail = "ada@work.example";
            Assert.False(page.HasLocalError);
            Assert.True(page.SaveLocalCommand.CanExecute(null));

            await page.SaveLocalCommand.ExecuteAsync(null);

            Assert.Equal(Work, services.Identity.LocalOf(WorkRepository.WorkTreePath));
            Assert.Equal([WorkRepository.WorkTreePath], services.Identity.LocalWrites);
            Assert.Equal(Work, page.LocalIdentity);
            Assert.Equal("Ada Lovelace", page.LocalName);
            Assert.False(page.IsLocalChanged);
            Assert.True(page.RemoveLocalCommand.CanExecute(null));

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("work has its own identity", note.Title);

            Assert.Equal(Home, services.Identity.Global);
            Assert.Equal(0, services.Identity.GlobalWrites);
        });
    }

    [Fact]
    public void RemovingTheRepositorysIdentityPutsItBackOnTheGlobalOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Home;
            services.Identity.SetLocalDirectly(WorkRepository.WorkTreePath, Work);
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            await page.RemoveLocalCommand.ExecuteAsync(null);

            Assert.Equal(GitIdentity.Empty, services.Identity.LocalOf(WorkRepository.WorkTreePath));
            Assert.False(page.HasLocalIdentity);
            Assert.Equal(string.Empty, page.LocalName);
            Assert.Equal(string.Empty, page.LocalEmail);
            Assert.False(page.RemoveLocalCommand.CanExecute(null));

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("work uses the global identity again", note.Title);
            Assert.Equal("Its commits are made as Ada <ada@home.example>.", note.Message);
            Assert.Equal(0, services.Identity.GlobalWrites);
        });
    }

    [Fact]
    public void CopyingFromTheCurrentProfileFillsTheFieldsWithoutWriting()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            await store.SaveAsync(IdentityProfile.Create("Home", Home));
            await store.SaveAsync(IdentityProfile.Create("Work", Work));
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Equal("Work", page.CurrentProfile?.Label);
            Assert.True(page.CopyFromCurrentProfileCommand.CanExecute(null));

            page.CopyFromCurrentProfileCommand.Execute(null);

            Assert.Equal("Ada Lovelace", page.LocalName);
            Assert.Equal("ada@work.example", page.LocalEmail);

            // Filled, not written: Save is what writes.
            Assert.Empty(services.Identity.LocalWrites);
            Assert.True(page.IsLocalChanged);
            Assert.True(page.SaveLocalCommand.CanExecute(null));

            await page.SaveLocalCommand.ExecuteAsync(null);

            Assert.Equal(Work, services.Identity.LocalOf(WorkRepository.WorkTreePath));
        });
    }

    [Fact]
    public void CopyingFollowsWhichProfileIsCurrent()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Ada;
            IIdentityProfileStore store = services.Get<IIdentityProfileStore>();
            await store.SaveAsync(IdentityProfile.Create("Home", Home));
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            // No profile is git's identity: nothing to copy.
            Assert.False(page.CopyFromCurrentProfileCommand.CanExecute(null));

            await page.UseProfileCommand.ExecuteAsync(page.Profiles[0]);

            Assert.True(page.CopyFromCurrentProfileCommand.CanExecute(null));

            page.CopyFromCurrentProfileCommand.Execute(null);

            Assert.Equal("Ada", page.LocalName);
            Assert.Equal("ada@home.example", page.LocalEmail);
        });
    }

    [Fact]
    public void AnotherRepositoryBringsItsOwnIdentityAndClosingItEmptiesTheSection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.SetLocalDirectly(WorkRepository.WorkTreePath, Work);
            services.Identity.SetLocalDirectly(HomeRepository.WorkTreePath, Home);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            page.LocalName = "Half typed for work";

            await context.OpenAsync(HomeRepository);

            // What was typed for the other repository goes with it.
            Assert.Equal("home", page.RepositoryName);
            Assert.Equal(Home, page.LocalIdentity);
            Assert.Equal("Ada", page.LocalName);
            Assert.False(page.IsLocalChanged);

            context.Close();

            Assert.False(page.IsRepositoryOpen);
            Assert.Equal(GitIdentity.Empty, page.LocalIdentity);
            Assert.Equal(string.Empty, page.LocalName);
            Assert.False(page.SaveLocalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AFailedRepositoryWriteIsReportedAndKeepsWhatWasTyped()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            services.Identity.Failure = FakeGitIdentityService.LockFailure();
            page.LocalName = "Ada Lovelace";
            page.LocalEmail = "ada@work.example";

            await page.SaveLocalCommand.ExecuteAsync(null);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not change this repository's identity", note.Title);
            Assert.StartsWith("error: could not lock config file", note.Message, StringComparison.Ordinal);
            Assert.Equal("Ada Lovelace", page.LocalName);
            Assert.False(page.HasLocalIdentity);
            Assert.True(page.SaveLocalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AFailedRepositoryReadIsReported()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            services.Identity.Failure = FakeGitIdentityService.LockFailure();

            await page.OnAppearingAsync();

            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Could not read this repository's identity");
        });
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

    [Fact]
    public void TheViewShowsTheRepositorySectionOnlyInARepositorysWindow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Identity.Global = Work;
            services.Identity.SetLocalDirectly(WorkRepository.WorkTreePath, Home);
            await services.Get<IIdentityProfileStore>().SaveAsync(IdentityProfile.Create("Work", Work));

            IdentityPageViewModel page = services.Get<IdentityPageViewModel>();
            await page.OnAppearingAsync();

            IdentityPageView view = services.Get<IdentityPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1000, Height = 1100 };
            window.Show();

            try
            {
                // The start window's case: no repository, no section — a hidden card has not even
                // built what it holds.
                Assert.DoesNotContain(All<TextBox>(view, "This repository's name"), box => box.IsEffectivelyVisible);

                await services.Get<IRepositoryContext>().OpenAsync(WorkRepository);
                window.UpdateLayout();

                TextBox name = Named<TextBox>(view, "This repository's name");
                Assert.True(name.IsEffectivelyVisible);
                Assert.Equal("Ada", name.Text);
                Assert.Equal("ada@home.example", Named<TextBox>(view, "This repository's email").Text);

                Button copy = Named<Button>(view, "Copy from the current profile");
                Button remove = Named<Button>(view, "Remove this repository's identity");
                Button save = Named<Button>(view, "Save this repository's identity");

                Assert.True(copy.IsEffectivelyEnabled);
                Assert.True(remove.IsEffectivelyEnabled);
                Assert.False(save.IsEffectivelyEnabled);

                copy.Command!.Execute(null);

                Assert.Equal("Ada Lovelace", name.Text);
                Assert.True(save.IsEffectivelyEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

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
