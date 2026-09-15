using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the stash as the changes page presents it: putting work aside, listing it, reading it and
/// getting it back.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class StashPanelTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public StashPanelTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "work"), "main");

        Write(repository, "README.md", "# one\n");
        Write(repository, "src/app.txt", "one\ntwo\n");

        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Add the initial files");

        return repository;
    }

    private static void Write(RepositoryHandle repository, string relativePath, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static async Task<ChangesPageViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        ChangesPageViewModel page = services.Get<ChangesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    // ---------------------------------------------------------------- putting work aside

    [Fact]
    public void Page_HasNothingStashedToStartWith()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            ChangesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.False(page.HasStashes);
            Assert.Empty(page.Stashes);
            Assert.False(page.StashAllCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_StashesEverythingAndComesBackClean()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");
            Write(repository, "notes.txt", "never committed\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            Assert.True(page.StashAllCommand.CanExecute(null));

            await page.StashAllCommand.ExecuteAsync(null);

            Assert.True(page.IsClean);
            Assert.True(page.HasStashes);
            Assert.Equal("1 stash", page.StashSummary);

            // Untracked files go with it; leaving them behind would make "stashed" a half-truth.
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "notes.txt")));
        });
    }

    [Fact]
    public void Page_ListsWhatEachEntrySaysAboutItself()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "first\n");
            await GitAsync(repository, "stash", "push", "-m", "the first one");

            Write(repository, "src/app.txt", "second\n");
            await GitAsync(repository, "stash", "push", "-m", "the second one");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            Assert.Equal(2, page.Stashes.Count);
            Assert.Equal("2 stashes", page.StashSummary);

            StashRowViewModel newest = page.Stashes[0];

            Assert.Equal("the second one", newest.Message);
            Assert.Equal("main", newest.Branch);
            Assert.True(newest.HasBranch);
            Assert.NotEqual(string.Empty, newest.When);
        });
    }

    // ---------------------------------------------------------------- getting it back

    [Fact]
    public void Page_AppliesAnEntryAndKeepsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");
            await GitAsync(repository, "stash", "push", "-m", "work");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            await page.ApplyStashCommand.ExecuteAsync(page.Stashes[0]);

            Assert.Equal(
                "one\ntwo edited\n",
                await File.ReadAllTextAsync(Path.Combine(repository.WorkTreePath, "src", "app.txt")));

            Assert.True(page.HasStashes);
        });
    }

    [Fact]
    public void Page_RestoresAnEntryAndRemovesIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");
            await GitAsync(repository, "stash", "push", "-m", "work");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            await page.PopStashCommand.ExecuteAsync(page.Stashes[0]);

            Assert.Equal(
                "one\ntwo edited\n",
                await File.ReadAllTextAsync(Path.Combine(repository.WorkTreePath, "src", "app.txt")));

            Assert.False(page.HasStashes);
            Assert.False(page.IsClean);
        });
    }

    // ---------------------------------------------------------------- dropping

    [Fact]
    public void Page_AsksBeforeDroppingAnEntry()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");
            await GitAsync(repository, "stash", "push", "-m", "work");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Close;

            await page.DropStashCommand.ExecuteAsync(page.Stashes[0]);

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            // Dropping is the only stash action that loses the work for good, and the question says so.
            Assert.Contains("cannot be recovered", message, StringComparison.Ordinal);
            Assert.Contains("work", message, StringComparison.Ordinal);
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
            Assert.True(page.HasStashes);
        });
    }

    [Fact]
    public void Page_DropsTheEntryItWasGivenOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "first\n");
            await GitAsync(repository, "stash", "push", "-m", "the first one");

            Write(repository, "src/app.txt", "second\n");
            await GitAsync(repository, "stash", "push", "-m", "the second one");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Primary;

            // The older one, which is exactly where an off-by-one would lose the wrong work.
            await page.DropStashCommand.ExecuteAsync(page.Stashes[1]);

            StashRowViewModel remaining = Assert.Single(page.Stashes);

            Assert.Equal("the second one", remaining.Message);
        });
    }

    // ---------------------------------------------------------------- reading one

    [Fact]
    public void Page_ShowsWhatAnEntryHoldsWithoutRestoringIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");
            await GitAsync(repository, "stash", "push", "-m", "work");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedStash = page.Stashes[0];
            await WaitUntilAsync(() => page.StashFiles.FileCount > 0);

            Assert.True(page.HasSelectedStash);
            Assert.Equal(1, page.StashFiles.FileCount);

            page.StashFiles.SelectPath("src/app.txt");
            await WaitUntilAsync(() => page.Diff.HasPatch);

            Assert.Equal("src/app.txt", page.Diff.Title);
            Assert.Contains(page.Diff.UnifiedRows, row => row.Single?.Text == "two edited");

            // Looking is not restoring: the work tree is untouched.
            Assert.True(page.IsClean);
        });
    }

    [Fact]
    public void Page_ShowsTheUntrackedFilesAnEntryTook()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "notes.txt", "never committed\n");
            await GitAsync(repository, "stash", "push", "--include-untracked", "-m", "work");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedStash = page.Stashes[0];
            await WaitUntilAsync(() => page.StashFiles.FileCount > 0);

            Assert.Contains(
                Enigma.GitClient.App.ViewModels.Panels.ChangedFilesPanelViewModel.Flatten(page.StashFiles.Nodes),
                node => node.Path == "notes.txt");
        });
    }

    [Fact]
    public void Page_ClearsTheStashFilesWhenNothingIsSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");
            await GitAsync(repository, "stash", "push", "-m", "work");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedStash = page.Stashes[0];
            await WaitUntilAsync(() => page.StashFiles.FileCount > 0);

            page.SelectedStash = null;
            await WaitUntilAsync(() => page.StashFiles.FileCount == 0);

            Assert.False(page.HasSelectedStash);
            Assert.True(page.StashFiles.IsEmpty);
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_DrawsTheStashSection()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                RepositoryHandle repository = await BuildRepositoryAsync(services);

                Write(repository, "src/app.txt", "one\ntwo stashed\n");
                await GitAsync(repository, "stash", "push", "-m", "half a thought");

                Write(repository, "README.md", "# edited\n");

                ChangesPageViewModel page = await OpenAsync(services, repository);

                ChangesPageView view = services.Get<ChangesPageView>();
                view.DataContext = page;

                Window window = new() { Content = view, Width = 1280, Height = 800 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "changes-page-stash.png");

                int colours = 0;

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    window.UpdateLayout();
                    window.InvalidateVisual();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The changes page produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);

                    if (colours >= 8)
                    {
                        break;
                    }
                }

                Assert.True(colours >= 8, "the changes page drew nothing");

                string[] texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("1 stash", texts);

                window.Content = null;
                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }
    }
}
