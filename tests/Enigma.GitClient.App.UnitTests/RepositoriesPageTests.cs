using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the landing page: its recent list, and the validation behind its clone and create dialogs.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoriesPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoriesPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void Page_StartsEmptyAndFillsFromTheStore()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            Assert.False(page.HasRepositories);

            IRepositoryListStore store = services.Get<IRepositoryListStore>();
            await store.AddAsync(IdentityProfile.DefaultId, Path.Combine(services.ConfigurationRoot, "one"), "one");
            await page.ReloadListAsync();

            Assert.True(page.HasRepositories);
            Assert.Equal("one", Assert.Single(page.Repositories).Name);
        });
    }

    [Fact]
    public void AtTheFirstStart_TheDefaultProfileIsCreatedAndItsListShown()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            await page.OnAppearingAsync();

            IdentityProfile profile = Assert.Single(await services.Get<IIdentityProfileStore>().GetAllAsync());
            Assert.Equal(IdentityProfile.DefaultId, profile.Id);
            Assert.Equal("Default", profile.Label);
            Assert.False(profile.HasIdentity);
            Assert.False(page.HasRepositories);
        });
    }

    [Fact]
    public void ThePageShowsOnlyTheSelectedProfilesRepositories()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));

            IRepositoryListStore store = services.Get<IRepositoryListStore>();
            await store.AddAsync(work.Id, Path.Combine(services.ConfigurationRoot, "work-repo"), "work-repo");
            await store.AddAsync(home.Id, Path.Combine(services.ConfigurationRoot, "home-repo"), "home-repo");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            // Nothing chosen yet: the first profile's list.
            await page.ReloadListAsync();
            Assert.Equal("work-repo", Assert.Single(page.Repositories).Name);

            services.Get<IProfileSelection>().Select(home.Id);
            await page.ReloadListAsync();
            Assert.Equal("home-repo", Assert.Single(page.Repositories).Name);
        });
    }

    [Fact]
    public void ThePickerListsEveryProfile_WithTheRememberedOneSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));
            services.Get<IProfileSelection>().Select(home.Id);

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Equal(["Work", "Home"], page.Profiles.Select(profile => profile.Label));
            Assert.Equal(home, page.SelectedProfile);
        });
    }

    [Fact]
    public void PickingAnotherProfile_ShowsItsListAndIsRemembered()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));

            IRepositoryListStore store = services.Get<IRepositoryListStore>();
            await store.AddAsync(work.Id, Path.Combine(services.ConfigurationRoot, "work-repo"), "work-repo");
            await store.AddAsync(home.Id, Path.Combine(services.ConfigurationRoot, "home-repo"), "home-repo");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.OnAppearingAsync();
            Assert.Equal("work-repo", Assert.Single(page.Repositories).Name);

            page.SelectedProfile = page.Profiles.Single(profile => profile.Id == home.Id);

            await WaitUntilAsync(() => page.Repositories.Count == 1 && page.Repositories[0].Name == "home-repo");
            Assert.Equal(home.Id, services.Get<ISettingsService>().Current.SelectedProfileId);

            // A page built afresh — the next start — opens on the same profile.
            RepositoriesPageViewModel again = ActivatorUtilities.CreateInstance<RepositoriesPageViewModel>(services.Provider);
            await again.OnAppearingAsync();

            Assert.Equal(home, again.SelectedProfile);
            Assert.Equal("home-repo", Assert.Single(again.Repositories).Name);
        });
    }

    [Fact]
    public void ShowingThePageAgain_PicksUpProfilesChangedElsewhere()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));
            services.Get<IProfileSelection>().Select(home.Id);

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.OnAppearingAsync();

            // On the Profiles page: Work renamed, Home deleted, Client added.
            await profiles.SaveAsync(work with { Label = "Office" });
            await profiles.RemoveAsync(home.Id);
            await profiles.SaveAsync(IdentityProfile.Create("Client", GitIdentity.Empty));

            await page.OnAppearingAsync();

            Assert.Equal(["Office", "Client"], page.Profiles.Select(profile => profile.Label));
            Assert.Equal(work.Id, page.SelectedProfile?.Id);
        });
    }

    [Fact]
    public void ARepositoryOpenedWhileAProfileIsPicked_JoinsThatProfilesList()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);
            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "picked"), "main");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.OnAppearingAsync();
            page.SelectedProfile = page.Profiles.Single(profile => profile.Id == home.Id);

            await page.OpenPathAsync(repository.WorkTreePath);

            IRepositoryListStore store = services.Get<IRepositoryListStore>();
            Assert.Equal("picked", Assert.Single(await store.GetAsync(home.Id)).Name);
            Assert.Empty(await store.GetAsync(work.Id));
        });
    }

    [Fact]
    public void ThePickerIsInThePagesHeader()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            await services.Get<IIdentityProfileStore>().SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));

            RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
            await model.OnAppearingAsync();

            RepositoriesPageView page = services.Get<RepositoriesPageView>();
            page.DataContext = model;

            Window window = new() { Content = page, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ComboBox picker = page.GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(box => AutomationProperties.GetName(box) == "The profile whose repositories are listed");

            Assert.Equal(1, picker.ItemCount);
            Assert.Equal("Work", Assert.IsType<IdentityProfile>(picker.SelectedItem).Label);

            window.Close();
        });
    }

    [Fact]
    public void TheThemeSwitch_SwitchesTheWholeApplicationAndRecordsTheChoice()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            ISettingsService settings = services.Get<ISettingsService>();

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;

                page.ToggleThemeCommand.Execute(null);
                Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);
                Assert.Equal(ThemePreference.Light, settings.Current.Theme);

                page.ToggleThemeCommand.Execute(null);
                Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
                Assert.Equal(ThemePreference.Dark, settings.Current.Theme);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void TheThemeSwitchSitsJustLeftOfAbout()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
            await model.OnAppearingAsync();

            RepositoriesPageView page = services.Get<RepositoriesPageView>();
            page.DataContext = model;

            Window window = new() { Content = page, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Button theme = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ToggleTheme");
            Button about = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OpenAbout");

            Assert.Same(model.ToggleThemeCommand, theme.Command);
            Assert.Equal("Switch between the dark and light themes", AutomationProperties.GetName(theme));

            StackPanel strip = Assert.IsType<StackPanel>(about.Parent);
            Assert.Equal(strip.Children.IndexOf(about) - 1, strip.Children.IndexOf(theme));

            Point themeAt = theme.TranslatePoint(default, page) ?? default;
            Point aboutAt = about.TranslatePoint(default, page) ?? default;
            Assert.True(themeAt.X < aboutAt.X, "the theme switch must be drawn left of About");

            window.Close();
        });
    }

    [Fact]
    public void OpeningAListedRepositoryAgain_LeavesItWhereItIs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            IRepositoryService repositories = services.Get<IRepositoryService>();
            RepositoryHandle first = await repositories.InitAsync(Path.Combine(root, "first"), "main");
            RepositoryHandle second = await repositories.InitAsync(Path.Combine(root, "second"), "main");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.OpenPathAsync(first.WorkTreePath);
            await page.OpenPathAsync(second.WorkTreePath);
            await page.OpenPathAsync(first.WorkTreePath);

            Assert.Equal(["first", "second"], page.Repositories.Select(entry => entry.Name));
        });
    }

    [Fact]
    public void OpeningARepository_PublishesItRemembersItAndShowsTheHistory()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            // A real repository, because opening one goes through git's own discovery.
            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "my-repo"), "main");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            IRepositoryContext context = services.Get<IRepositoryContext>();

            bool opened = await page.OpenPathAsync(repository.WorkTreePath);

            Assert.True(opened);
            Assert.Equal("my-repo", context.Repository?.Name);
            Assert.Equal([AppWindowKind.Repository], services.Windows.Requested);
            Assert.Contains(page.Repositories, entry => entry.Name == "my-repo");
        });
    }

    [Fact]
    public void OpeningSomethingThatIsNotARepository_ReportsItAndChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            string plain = Path.Combine(services.ConfigurationRoot, "not-a-repository");
            Directory.CreateDirectory(plain);

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            IRepositoryContext context = services.Get<IRepositoryContext>();

            bool opened = await page.OpenPathAsync(plain);

            Assert.False(opened);
            Assert.False(context.IsRepositoryOpen);
            Assert.Empty(page.Repositories);

            Assert.Equal(Enigma.Avalonia.Desktop.Controls.InfoBar.InfoBarSeverity.Warning, services.InfoBar.Last?.Severity);
            Assert.Contains("not inside a git repository", services.InfoBar.Last!.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CloningIntoTheWorkspace_OpensTheCloneAndRemembersIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            IRepositoryService repositories = services.Get<IRepositoryService>();
            RepositoryHandle source = await repositories.InitAsync(Path.Combine(root, "source"), "main");

            File.WriteAllText(Path.Combine(source.WorkTreePath, "README.md"), "# source\n");
            RunGit(source.WorkTreePath, "add", "--all");
            RunGit(source.WorkTreePath, "-c", "user.name=T", "-c", "user.email=t@e.invalid", "commit", "-m", "base");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            bool cloned = await page.RunCloneAsync(new CloneRequest
            {
                Url = source.WorkTreePath,
                ParentDirectory = root,
                DirectoryName = "cloned",
            });

            Assert.True(cloned);
            Assert.Equal("cloned", services.Get<IRepositoryContext>().Repository?.Name);
            Assert.Contains(page.Repositories, entry => entry.Name == "cloned");

            // The progress overlay must go up and, above all, come back down.
            Assert.Equal(1, services.Overlay.ShowCount);
            Assert.False(services.Overlay.IsOpen, "an overlay left open makes the window unusable");
        });
    }

    [Fact]
    public void ACloneThatWorked_IsWhereTheNextCloneIsSuggested()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            string root = Path.Combine(services.ConfigurationRoot, "clones");
            RepositoryHandle source = await SourceRepositoryAsync(services);
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            // Nothing cloned yet: the home folder, as before.
            Assert.Equal(HomeFolder, page.CloneParentDirectory());

            Assert.True(await page.RunCloneAsync(new CloneRequest
            {
                Url = source.WorkTreePath,
                ParentDirectory = root,
                DirectoryName = "cloned",
            }));

            Assert.Equal(Path.GetFullPath(root), services.Get<ISettingsService>().Current.CloneParentDirectory);

            // The next clone dialog opens on it.
            CloneRepositoryDialogViewModel? next = null;
            services.Dialogs.OnShown = dialog => next = ((Control)dialog.Content!).DataContext as CloneRepositoryDialogViewModel;

            await page.CloneCommand.ExecuteAsync(null);

            Assert.NotNull(next);
            Assert.Equal(Path.GetFullPath(root), next.ParentDirectory);
        });
    }

    [Fact]
    public void ACloneThatFailed_LeavesTheSuggestionWhereItWas()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            string root = Path.Combine(services.ConfigurationRoot, "clones");
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            Assert.False(await page.RunCloneAsync(new CloneRequest
            {
                Url = Path.Combine(services.ConfigurationRoot, "no-such-repository"),
                ParentDirectory = root,
                DirectoryName = "cloned",
            }));

            Assert.Equal(string.Empty, services.Get<ISettingsService>().Current.CloneParentDirectory);
            Assert.Equal(HomeFolder, page.CloneParentDirectory());
        });
    }

    [Fact]
    public void ARememberedDirectoryThatIsGone_IsNotSuggested()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            string kept = Path.Combine(services.ConfigurationRoot, "kept");
            Directory.CreateDirectory(kept);

            ISettingsService settings = services.Get<ISettingsService>();
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            settings.Update(current => current with { CloneParentDirectory = kept });
            Assert.Equal(kept, page.CloneParentDirectory());

            settings.Update(current => current with { CloneParentDirectory = Path.Combine(services.ConfigurationRoot, "unplugged") });
            Assert.Equal(HomeFolder, page.CloneParentDirectory());
        });
    }

    [Fact]
    public void ForgettingARepository_RemovesItFromTheListOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryListStore store = services.Get<IRepositoryListStore>();

            string path = Path.Combine(services.ConfigurationRoot, "kept");
            Directory.CreateDirectory(path);
            await store.AddAsync(IdentityProfile.DefaultId, path, "kept");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.ReloadListAsync();

            await page.ForgetCommand.ExecuteAsync(page.Repositories.Single());

            Assert.Empty(page.Repositories);
            Assert.True(Directory.Exists(path), "forgetting must never delete the repository");
        });
    }

    // ---------------------------------------------------------------- dialog validation

    [Fact]
    public void CloneDialog_ValidatesTheUrlAndSuggestsTheDirectoryName()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            CloneRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot);

            Assert.False(model.IsValid);

            model.Url = "https://github.com/owner/repository.git";

            Assert.Equal("repository", model.DirectoryName);
            Assert.True(model.IsValid);
            Assert.Equal(string.Empty, model.ValidationMessage);
            Assert.Equal(Path.Combine(services.ConfigurationRoot, "repository"), model.TargetPathPreview);

            model.Url = "telnet://example.com/repo";

            Assert.False(model.IsValid);
            Assert.Contains("telnet", model.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void CloneDialog_StopsSuggestingOnceTheUserTypesADirectoryName()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            CloneRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot);

            model.Url = "https://github.com/owner/first.git";
            model.DirectoryName = "chosen-by-hand";
            model.Url = "https://github.com/owner/second.git";

            Assert.Equal("chosen-by-hand", model.DirectoryName);
        });
    }

    [Fact]
    public void CloneDialog_BuildsTheRequestItDescribes()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            CloneRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot)
            {
                Url = "  https://github.com/owner/repository.git  ",
                Branch = " develop ",
                IsShallow = true,
                Depth = 25,
                RecurseSubmodules = true,
            };

            CloneRequest request = model.ToRequest();

            Assert.Equal("https://github.com/owner/repository.git", request.Url);
            Assert.Equal("develop", request.Branch);
            Assert.Equal(25, request.Depth);
            Assert.True(request.RecurseSubmodules);
        });
    }

    [Fact]
    public void CloneDialog_DropsTheDepthWhenTheCloneIsNotShallow()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            CloneRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot)
            {
                Url = "https://github.com/owner/repository.git",
                IsShallow = false,
                Depth = 25,
            };

            Assert.Null(model.ToRequest().Depth);
            Assert.False(model.IsDepthEnabled);
        });
    }

    [Fact]
    public void CloneDialog_RefusesADirectoryThatAlreadyHasContent()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            string occupied = Path.Combine(services.ConfigurationRoot, "occupied");
            Directory.CreateDirectory(occupied);
            File.WriteAllText(Path.Combine(occupied, "file.txt"), "content");

            CloneRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot)
            {
                Url = "https://github.com/owner/repository.git",
                DirectoryName = "occupied",
            };

            Assert.False(model.IsValid);
            Assert.Contains("not empty", model.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void InitDialog_ValidatesTheBranchName()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            InitRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.ConfigurationRoot)
            {
                DirectoryName = "my-project",
            };

            Assert.True(model.IsValid);
            Assert.Equal("main", model.InitialBranch);

            model.InitialBranch = "has a space";
            Assert.False(model.IsValid);

            model.InitialBranch = "feature/nested-name";
            Assert.True(model.IsValid);
        });
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("feature/nested", true)]
    [InlineData("release-1.0", true)]
    [InlineData("", false)]
    [InlineData("-leading-dash", false)]
    [InlineData("/leading-slash", false)]
    [InlineData("trailing-slash/", false)]
    [InlineData("double..dot", false)]
    [InlineData("double//slash", false)]
    [InlineData("has space", false)]
    [InlineData("has~tilde", false)]
    [InlineData("has^caret", false)]
    [InlineData("has:colon", false)]
    [InlineData("has?question", false)]
    [InlineData("has*star", false)]
    [InlineData("has[bracket", false)]
    [InlineData("has@{reflog", false)]
    [InlineData("@", false)]
    [InlineData("ends.lock", false)]
    public void BranchNameCheck_MatchesGitsRules(string name, bool expected)
        => Assert.Equal(expected, InitRepositoryDialogViewModel.IsPlausibleBranchName(name));

    [Fact]
    public void InitDialog_RefusesADirectoryThatIsAlreadyARepository()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            string existing = Path.Combine(services.ConfigurationRoot, "already");
            Directory.CreateDirectory(Path.Combine(existing, ".git"));

            InitRepositoryDialogViewModel model = new(
                services.Get<Enigma.Avalonia.Desktop.Services.IFolderDialogService>(),
                services.ConfigurationRoot)
            {
                DirectoryName = "already",
            };

            Assert.False(model.IsValid);
            Assert.Contains("already a git repository", model.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_RendersItsListOffScreen()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build();
                IRepositoryListStore store = services.Get<IRepositoryListStore>();

                foreach (string name in new[] { "Enigma.GitClient", "Enigma.Avalonia", "Enigma.Icons" })
                {
                    string path = Path.Combine(services.ConfigurationRoot, "src", name);
                    Directory.CreateDirectory(path);
                    await store.AddAsync(IdentityProfile.DefaultId, path, name);
                }

                RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
                await model.ReloadListAsync();

                RepositoriesPageView page = services.Get<RepositoriesPageView>();
                page.DataContext = model;

                Window window = new() { Content = page, Width = 1100, Height = 700 };
                window.Show();

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();
                }

                using Bitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The page produced no rendered frame.");

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, "repositories-page.png"), PngBitmapEncoderOptions.Default);

                string[] texts = [.. page.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("Enigma.GitClient", texts);
                Assert.Contains("Enigma.Icons", texts);

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void EmptyPage_CentresItsEmptyStateOnThePage()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
            await model.ReloadListAsync();

            Assert.False(model.HasRepositories);

            RepositoriesPageView page = services.Get<RepositoriesPageView>();
            page.DataContext = model;

            // The start window's own size.
            Window window = new() { Content = page, Width = 1100, Height = 720 };
            window.Show();

            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                EmptyState empty = page.GetVisualDescendants().OfType<EmptyState>().Single();
                Assert.True(empty.IsVisible);

                // The empty state has the page's whole width below the header, not the width of its
                // text against the left edge.
                Assert.Equal(page.Bounds.Width, empty.Bounds.Width, tolerance: 1.0);

                // And what it says sits in the middle of that area, both ways.
                StackPanel content = empty.GetVisualDescendants().OfType<StackPanel>().First();
                Point centre = content.TranslatePoint(new Point(content.Bounds.Width / 2, content.Bounds.Height / 2), empty)
                    ?? throw new InvalidOperationException("The empty state's content is not under it.");

                Assert.Equal(empty.Bounds.Width / 2, centre.X, tolerance: 1.0);
                Assert.Equal(empty.Bounds.Height / 2, centre.Y, tolerance: 1.0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // Where a clone goes when there is nothing to remember.
    private static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// A repository with one commit, to clone from.
    /// </summary>
    private static async Task<RepositoryHandle> SourceRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle source = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "source"), "main");

        File.WriteAllText(Path.Combine(source.WorkTreePath, "README.md"), "# source\n");
        RunGit(source.WorkTreePath, "add", "--all");
        RunGit(source.WorkTreePath, "-c", "user.name=T", "-c", "user.email=t@e.invalid", "commit", "-m", "base");

        return source;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the page never reached the state the test waited for");
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
        }
    }
}
