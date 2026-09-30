using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the working directory page against a real repository: staging, unstaging, discarding and
/// committing, each asserted against what git ended up recording.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ChangesPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ChangesPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

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

    private static async Task<string> ReadGitAsync(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.TrimEnd('\n', '\r');
    }

    private static async Task<ChangesPageViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        ChangesPageViewModel page = services.Get<ChangesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static IReadOnlyList<string> PathsOf(ChangedFilesPanelViewModel panel)
        => [.. ChangedFilesPanelViewModel.Flatten(panel.Nodes)
            .Where(node => !node.IsDirectory)
            .Select(node => node.Path)];

    private static ChangedFileNodeViewModel Row(ChangedFilesPanelViewModel panel, string path)
        => ChangedFilesPanelViewModel.Flatten(panel.Nodes).Single(node => node.Path == path);

    // ---------------------------------------------------------------- what the page shows

    [Fact]
    public void Page_IsEmptyWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ChangesPageViewModel page = services.Get<ChangesPageViewModel>();

            Assert.True(page.IsClean);
            Assert.Contains("Open a repository", page.EmptyMessage, StringComparison.Ordinal);
            Assert.False(page.CommitCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_SaysSoWhenTheTreeIsClean()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            ChangesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.True(page.IsClean);
            Assert.False(page.HasStaged);
            Assert.False(page.HasUnstaged);
            Assert.Equal("main", page.BranchName);
            Assert.Contains("Nothing has changed", page.EmptyMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Page_SplitsTheChangeIntoStagedAndNotStaged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            await GitAsync(repository, "add", "src/app.txt");
            Write(repository, "README.md", "# edited\n");
            Write(repository, "notes.txt", "brand new\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            Assert.Equal(["src/app.txt"], PathsOf(page.Staged));
            Assert.Equal(["README.md", "notes.txt"], PathsOf(page.Unstaged).Order(StringComparer.Ordinal));
            Assert.True(page.HasStaged);
            Assert.True(page.HasUnstaged);
            Assert.False(page.IsClean);
        });
    }

    [Fact]
    public void Page_ShowsTheUpstreamAndHowFarApart()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            string originPath = Path.Combine(services.ConfigurationRoot, "workspace", "origin.git");
            Directory.CreateDirectory(originPath);

            await GitAsync(repository, "init", "--bare", originPath);
            await GitAsync(repository, "remote", "add", "origin", originPath);
            await GitAsync(repository, "push", "--set-upstream", "origin", "main");

            Write(repository, "src/ahead.txt", "ahead\n");
            await GitAsync(repository, "add", "--all");
            await GitAsync(repository, "commit", "-m", "Move ahead");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            Assert.True(page.HasTrackingSummary);
            Assert.Contains("1 ahead", page.TrackingSummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Page_GivesBothPanelsTheirOwnVerbs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            // The panel is the history's own control; only the verbs on a row differ.
            Assert.Equal("Stage", page.Unstaged.Actions!.PrimaryLabel);
            Assert.Equal("Discard…", page.Unstaged.Actions.SecondaryLabel);
            Assert.Equal("Unstage", page.Staged.Actions!.PrimaryLabel);
            Assert.Null(page.Staged.Actions.Secondary);

            Assert.True(Row(page.Unstaged, "src/app.txt").HasActions);
        });
    }

    // ---------------------------------------------------------------- staging

    [Fact]
    public void Page_StagesOneFileAndMovesItAcross()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            await page.StageCommand.ExecuteAsync(Row(page.Unstaged, "src/app.txt"));

            Assert.Equal(["src/app.txt"], PathsOf(page.Staged));
            Assert.Equal(["README.md"], PathsOf(page.Unstaged));
        });
    }

    [Fact]
    public void Page_StagesAWholeDirectoryFromItsRow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/one.txt", "one\n");
            Write(repository, "src/two.txt", "two\n");
            Write(repository, "README.md", "# edited\n");

            // A directory row is the tree's: the list the page opens on has none.
            services.Get<ISettingsService>().Update(current => current with { FilesView = FilesView.Tree });

            ChangesPageViewModel page = await OpenAsync(services, repository);

            ChangedFileNodeViewModel directory = page.Unstaged.Nodes.Single(node => node.IsDirectory);

            await page.StageCommand.ExecuteAsync(directory);

            Assert.Equal(["src/one.txt", "src/two.txt"], PathsOf(page.Staged).Order());
            Assert.Equal(["README.md"], PathsOf(page.Unstaged));
        });
    }

    [Fact]
    public void Page_StagesAndUnstagesEverything()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            Write(repository, "notes.txt", "brand new\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            await page.StageAllCommand.ExecuteAsync(null);

            Assert.Equal(2, PathsOf(page.Staged).Count);
            Assert.Empty(PathsOf(page.Unstaged));

            await page.UnstageAllCommand.ExecuteAsync(null);

            Assert.Empty(PathsOf(page.Staged));
            Assert.Equal(2, PathsOf(page.Unstaged).Count);
        });
    }

    [Fact]
    public void Page_UnstagesOneFile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            await GitAsync(repository, "add", "--all");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            await page.UnstageCommand.ExecuteAsync(Row(page.Staged, "src/app.txt"));

            Assert.Empty(PathsOf(page.Staged));
            Assert.Equal(["src/app.txt"], PathsOf(page.Unstaged));
        });
    }

    // ---------------------------------------------------------------- discarding

    [Fact]
    public void Page_AsksBeforeDiscardingAndKeepsTheFileOnCancel()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Close;

            await page.DiscardCommand.ExecuteAsync(Row(page.Unstaged, "src/app.txt"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("src/app.txt", message, StringComparison.Ordinal);
            Assert.Contains("cannot be undone", message, StringComparison.Ordinal);
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
            Assert.Equal("ruined\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
        });
    }

    [Fact]
    public void Page_DiscardsOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Primary;

            await page.DiscardCommand.ExecuteAsync(Row(page.Unstaged, "src/app.txt"));

            Assert.Equal("one\ntwo\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
            Assert.True(page.IsClean);
        });
    }

    [Fact]
    public void Page_AsksOnePlainRedQuestionBeforeDiscardingEverything()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");
            Write(repository, "notes.txt", "never committed\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            bool redWhileAsked = false;
            services.Dialogs.OnShown = dialog => redWhileAsked = dialog.Classes.Contains(ContentDialogServiceExtensions.DangerClass);
            services.Dialogs.Result = DialogResult.Close;

            await page.DiscardAllCommand.ExecuteAsync(null);

            // One question, and a plain one: a sentence and two buttons, nothing to type.
            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            string message = Assert.IsType<string>(dialog.Content);

            Assert.Equal("Discard everything", dialog.Title);
            Assert.Contains("2 files", message, StringComparison.Ordinal);
            Assert.Contains("cannot be undone", message, StringComparison.Ordinal);
            Assert.Equal("Discard everything", dialog.PrimaryButtonText);
            Assert.Equal("Cancel", dialog.CloseButtonText);
            Assert.Equal(DefaultButton.Close, dialog.DefaultButton);
            Assert.True(dialog.IsPrimaryButtonEnabled);

            // Red while it was asked, and only then.
            Assert.True(redWhileAsked, "the confirm button was not red");
            Assert.DoesNotContain(ContentDialogServiceExtensions.DangerClass, dialog.Classes);

            // Cancelled, so both files are still there.
            Assert.Equal("ruined\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "notes.txt")));
        });
    }

    [Fact]
    public void Page_DiscardsEverythingOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");
            Write(repository, "notes.txt", "never committed\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Primary;

            await page.DiscardAllCommand.ExecuteAsync(null);

            Assert.Equal("one\ntwo\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "notes.txt")));
            Assert.True(page.IsClean);
        });
    }

    [Fact]
    public void Page_ConfirmsAOneFileDiscardInRedToo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            bool redWhileAsked = false;
            services.Dialogs.OnShown = dialog => redWhileAsked = dialog.Classes.Contains(ContentDialogServiceExtensions.DangerClass);
            services.Dialogs.Result = DialogResult.Close;

            await page.DiscardCommand.ExecuteAsync(Row(page.Unstaged, "src/app.txt"));

            Assert.True(redWhileAsked, "the confirm button was not red");
            Assert.Equal("Discard", services.Dialogs.Last!.PrimaryButtonText);
            Assert.DoesNotContain(ContentDialogServiceExtensions.DangerClass, services.Dialogs.Last.Classes);
        });
    }

    [Fact]
    public void DiscardEverythingButton_IsRedWhileThereIsSomethingToLose()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);
            (Window window, ChangesPageView view) = Show(services, page);

            try
            {
                Button discard = view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "DiscardAll");

                Assert.Contains("danger", discard.Classes);
                Assert.Same(page.DiscardAllCommand, discard.Command);
                Assert.True(discard.IsEffectivelyEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- committing

    [Fact]
    public void Page_WillNotCommitWithoutAMessageOrWithoutAnythingStaged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            // Nothing staged.
            page.Message = "A fine message";
            Assert.False(page.CommitCommand.CanExecute(null));

            await page.StageAllCommand.ExecuteAsync(null);
            Assert.True(page.CommitCommand.CanExecute(null));

            // Staged, but nothing to say about it.
            page.Message = "   ";
            Assert.False(page.CommitCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_CommitsWhatIsStagedAndClearsItself()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            await page.StageAllCommand.ExecuteAsync(null);

            page.Message = "Change the application file\n\nWith a body that explains why.";

            await page.CommitCommand.ExecuteAsync(null);

            Assert.Equal("Change the application file", await ReadGitAsync(repository, "log", "-1", "--format=%s"));
            Assert.True(page.IsClean);
            Assert.Equal(string.Empty, page.Message);

            // The short hash is what a user recognises the commit by afterwards.
            string sha = await ReadGitAsync(repository, "rev-parse", "--short=7", "HEAD");

            Assert.Contains(services.InfoBar.Shown, note => note.Message.StartsWith(sha, StringComparison.Ordinal));
        });
    }

    // ---------------------------------------------------------------- the message guides

    [Fact]
    public void CommitBox_HasNoAmendAndNoSignOff()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);
            (Window window, ChangesPageView view) = Show(services, page);

            try
            {
                // Nothing to tick: the box records a new commit and nothing else.
                Assert.Empty(view.GetVisualDescendants().OfType<CheckBox>());

                Button commit = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, page.CommitCommand));
                Assert.Equal("Commit", commit.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("Short and sweet", false)]
    [InlineData("A subject line that keeps going well past the point at which it stops summarising", true)]
    public void Page_SaysWhenTheSubjectHasRunOn(string message, bool expected)
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ChangesPageViewModel page = services.Get<ChangesPageViewModel>();

            page.Message = message;

            Assert.Equal(expected, page.IsSubjectTooLong);
            Assert.Equal(message.Length.ToString(System.Globalization.CultureInfo.CurrentCulture), page.SubjectLength);
        });
    }

    [Fact]
    public void Page_SaysWhenABodyLineWillNotWrapWell()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ChangesPageViewModel page = services.Get<ChangesPageViewModel>();

            page.Message = "Subject\n\nShort body line.";

            Assert.False(page.HasLongBodyLine);

            page.Message = "Subject\n\n" + new string('x', ChangesPageViewModel.BodyGuide + 1);

            Assert.True(page.HasLongBodyLine);

            // The subject is measured against its own guide, not the body's.
            Assert.Equal("Subject", page.Subject);
        });
    }

    // ---------------------------------------------------------------- the diff pane

    [Fact]
    public void Page_ShowsTheUnstagedDiffOfTheFilePicked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            page.Unstaged.SelectPath("src/app.txt");
            await WaitUntilAsync(() => page.Diff.HasPatch);

            Assert.Equal("src/app.txt", page.Diff.Title);
            Assert.Contains(page.Diff.UnifiedRows, row => row.Single?.Text == "two edited");
        });
    }

    [Fact]
    public void Page_ShowsANewUntrackedFileAsEveryLineAdded_NothingOnTheLeft()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/fresh.txt", "first\nsecond\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            page.Unstaged.SelectPath("src/fresh.txt");
            await WaitUntilAsync(() => page.Diff.HasPatch);

            // A diff, as a commit that added the file shows it — not "touches no lines of text".
            Assert.Equal(string.Empty, page.Diff.Message);

            DiffRowViewModel[] lines = [.. page.Diff.SideBySideRows.Where(row => row.Left is not null || row.Right is not null)];

            Assert.Equal(2, lines.Length);
            Assert.All(lines, row =>
            {
                Assert.True(row.Left!.IsFiller, "the left side should be empty");
                Assert.True(row.Right!.IsAdded, "the right side should be added");
            });
            Assert.Equal(["first", "second"], lines.Select(row => row.Right!.Text));
        });
    }

    [Fact]
    public void Page_SwitchesTheDiffToTheStagedHalfWhenThatSideIsPicked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo staged\n");
            await GitAsync(repository, "add", "--all");
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);

            page.Unstaged.SelectPath("README.md");
            await WaitUntilAsync(() => page.Diff.HasPatch);

            Assert.Equal("README.md", page.Diff.Title);

            page.Staged.SelectPath("src/app.txt");
            await WaitUntilAsync(() => page.Diff.Title == "src/app.txt");

            // Picking on one side clears the other: which half the diff belongs to has to be
            // unambiguous.
            Assert.Null(page.Unstaged.SelectedFile);
            Assert.Contains(page.Diff.UnifiedRows, row => row.Single?.Text == "two staged");
        });
    }

    // ---------------------------------------------------------------- the shell

    [Fact]
    public void BackButton_TakesYouToTheHistory()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // Building the shell is what wires the page's way back to the navigation.
            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            Assert.NotNull(shell);

            ChangesPageViewModel page = await OpenAsync(services, repository);
            IShellNavigation navigation = services.Get<IShellNavigation>();
            navigation.GoTo(ShellPage.Changes);

            Assert.True(page.BackToHistoryCommand.CanExecute(null));
            page.BackToHistoryCommand.Execute(null);

            Assert.Equal(ShellPage.History, navigation.Current);
        });
    }

    [Fact]
    public void BackButton_IsTheBlueOneAtTheHeadOfTheToolbar()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);
            int requested = 0;
            page.HistoryRequested += (_, _) => requested++;

            (Window window, ChangesPageView view) = Show(services, page);

            try
            {
                Button back = view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "BackToHistory");

                Assert.Contains("back", back.Classes);
                Assert.Same(page.BackToHistoryCommand, back.Command);

                // First in the header, before the page's title.
                TextBlock title = view.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == page.Title);
                Assert.True(
                    (back.TranslatePoint(default, view)?.X ?? double.MaxValue) < (title.TranslatePoint(default, view)?.X ?? 0),
                    "the back button is not before the title");

                Point middle = back.TranslatePoint(new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), window) ?? default;
                window.MouseDown(middle, MouseButton.Left);
                window.MouseUp(middle, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, requested);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Escape_GoesBackToTheHistoryWithoutAClickFirst()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);
            int requested = 0;
            page.HistoryRequested += (_, _) => requested++;

            (Window window, ChangesPageView view) = Show(services, page);

            try
            {
                // Nothing was clicked: the page took the focus when it was shown.
                Assert.True(view.IsKeyboardFocusWithin, "the page did not take the focus when it was shown");

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, requested);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Escape_GoesBackFromInsideTheCommitMessageAndKeepsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);
            page.Message = "Half a thought";
            int requested = 0;
            page.HistoryRequested += (_, _) => requested++;

            (Window window, ChangesPageView view) = Show(services, page);

            try
            {
                // A TextBox is exactly the kind of control that swallows a key it is offered.
                TextBox message = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "MessageBox");
                message.Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.True(message.IsFocused);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, requested);
                Assert.Equal("Half a thought", page.Message);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Escape_LeavesAQuestionAskedOverThePageAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            ChangesPageViewModel page = await OpenAsync(services, repository);
            int requested = 0;
            page.HistoryRequested += (_, _) => requested++;

            ChangesPageView view = services.Get<ChangesPageView>();
            view.DataContext = page;

            // As the window hosts it: the dialog over the page, which keeps the focus.
            ContentDialog dialog = new() { Title = "Discard everything", CloseButtonText = "Cancel" };
            Window window = new() { Content = new Panel { Children = { view, dialog } }, Width = 1200, Height = 700 };
            window.Show();

            try
            {
                Settle(window);
                _ = dialog.ShowAsync();
                Settle(window);

                Assert.True(view.IsKeyboardFocusWithin);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(0, requested);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (Window Window, ChangesPageView View) Show(TestServices services, ChangesPageViewModel page)
    {
        ChangesPageView view = services.Get<ChangesPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Settle(window);

        return (window, view);
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_DrawsBothHalvesAndTheCommitBox()
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

                Write(repository, "src/app.txt", "one\ntwo edited\n");
                await GitAsync(repository, "add", "src/app.txt");
                Write(repository, "README.md", "# edited\n");
                Write(repository, "notes.txt", "brand new\n");

                ChangesPageViewModel page = await OpenAsync(services, repository);
                page.Message = "Describe the change";

                page.Staged.SelectPath("src/app.txt");
                await WaitUntilAsync(() => page.Diff.HasPatch);

                ChangesPageView view = services.Get<ChangesPageView>();
                view.DataContext = page;

                Window window = new() { Content = view, Width = 1280, Height = 760 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "changes-page.png");

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

                Assert.Contains("Not staged", texts);
                Assert.Contains("Staged", texts);
                Assert.Contains("main", texts);
                Assert.Contains("app.txt", texts);
                Assert.Contains("README.md", texts);

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
