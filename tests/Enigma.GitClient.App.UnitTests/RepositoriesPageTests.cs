using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
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

            Assert.False(page.HasRecent);

            IRecentRepositoryStore store = services.Get<IRecentRepositoryStore>();
            await store.TouchAsync(Path.Combine(services.ConfigurationRoot, "one"), "one");
            await page.ReloadRecentAsync();

            Assert.True(page.HasRecent);
            Assert.Equal("one", Assert.Single(page.Recent).Name);
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
            Assert.Contains(page.Recent, entry => entry.Name == "my-repo");
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
            Assert.Empty(page.Recent);

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
            Assert.Contains(page.Recent, entry => entry.Name == "cloned");

            // The progress overlay must go up and, above all, come back down.
            Assert.Equal(1, services.Overlay.ShowCount);
            Assert.False(services.Overlay.IsOpen, "an overlay left open makes the window unusable");
        });
    }

    [Fact]
    public void ForgettingARepository_RemovesItFromTheListOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRecentRepositoryStore store = services.Get<IRecentRepositoryStore>();

            string path = Path.Combine(services.ConfigurationRoot, "kept");
            Directory.CreateDirectory(path);
            await store.TouchAsync(path, "kept");

            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();
            await page.ReloadRecentAsync();

            await page.ForgetRecentCommand.ExecuteAsync(page.Recent.Single());

            Assert.Empty(page.Recent);
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
    public void Page_RendersItsRecentListOffScreen()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build();
                IRecentRepositoryStore store = services.Get<IRecentRepositoryStore>();

                foreach (string name in new[] { "Enigma.GitClient", "Enigma.Avalonia", "Enigma.Icons" })
                {
                    string path = Path.Combine(services.ConfigurationRoot, "src", name);
                    Directory.CreateDirectory(path);
                    await store.TouchAsync(path, name);
                }

                RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
                await model.ReloadRecentAsync();

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
