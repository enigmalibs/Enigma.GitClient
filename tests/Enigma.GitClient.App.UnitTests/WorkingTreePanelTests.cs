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
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.App.Views.Panels;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the working tree against a real repository, as the history's details panel shows it for the
/// uncommitted line: staging, unstaging, discarding and committing, each asserted against what git
/// ended up recording, and the diffs of either half drawn over the graph.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WorkingTreePanelTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public WorkingTreePanelTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

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

    /// <summary>
    /// The panel on its own, as the history makes it when the uncommitted line is selected.
    /// </summary>
    private static async Task<WorkingTreePanelViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        WorkingTreePanelViewModel panel = services.Get<WorkingTreePanelViewModel>();
        panel.IsActive = true;
        await panel.RefreshAsync();

        return panel;
    }

    /// <summary>
    /// The history, with its uncommitted line selected and the working tree read into its panel.
    /// </summary>
    private static async Task<HistoryPageViewModel> OpenInTheHistoryAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.ReloadAsync();

        history.SelectedRow = history.Rows.Single(row => row.IsUncommitted);
        await WaitUntilAsync(() => history.WorkingTree.HasStaged || history.WorkingTree.HasUnstaged);

        return history;
    }

    private static IReadOnlyList<string> PathsOf(ChangedFilesPanelViewModel panel)
        => [.. ChangedFilesPanelViewModel.Flatten(panel.Nodes)
            .Where(node => !node.IsDirectory)
            .Select(node => node.Path)];

    private static ChangedFileNodeViewModel Row(ChangedFilesPanelViewModel panel, string path)
        => ChangedFilesPanelViewModel.Flatten(panel.Nodes).Single(node => node.Path == path);

    // ---------------------------------------------------------------- what the panel shows

    [Fact]
    public void Panel_IsEmptyWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            WorkingTreePanelViewModel panel = services.Get<WorkingTreePanelViewModel>();

            Assert.True(panel.IsClean);
            Assert.False(panel.CommitCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Panel_SaysSoWhenTheTreeIsClean()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            WorkingTreePanelViewModel panel = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.True(panel.IsClean);
            Assert.False(panel.HasStaged);
            Assert.False(panel.HasUnstaged);
        });
    }

    [Fact]
    public void Panel_SplitsTheChangeIntoStagedAndNotStaged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            await GitAsync(repository, "add", "src/app.txt");
            Write(repository, "README.md", "# edited\n");
            Write(repository, "notes.txt", "brand new\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            Assert.Equal(["src/app.txt"], PathsOf(panel.Staged));
            Assert.Equal(["README.md", "notes.txt"], PathsOf(panel.Unstaged).Order(StringComparer.Ordinal));
            Assert.True(panel.HasStaged);
            Assert.True(panel.HasUnstaged);
            Assert.False(panel.IsClean);
        });
    }

    [Fact]
    public void Panel_GivesBothListsTheirOwnVerbs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            // The lists are a commit's own control; only the verbs on a row differ.
            Assert.Equal("Stage", panel.Unstaged.Actions!.PrimaryLabel);
            Assert.Equal("Discard…", panel.Unstaged.Actions.SecondaryLabel);
            Assert.Equal("Unstage", panel.Staged.Actions!.PrimaryLabel);
            Assert.Null(panel.Staged.Actions.Secondary);

            Assert.True(Row(panel.Unstaged, "src/app.txt").HasActions);
        });
    }

    [Fact]
    public void Panel_ReadsNothingWhileTheHistoryDoesNotShowIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            WorkingTreePanelViewModel panel = services.Get<WorkingTreePanelViewModel>();

            // A refresh of the repository reads the tree only for a panel that is on screen.
            await services.Get<IRepositoryContext>().RefreshAsync();
            await Task.Delay(50);
            Assert.False(panel.HasUnstaged);

            panel.IsActive = true;
            await services.Get<IRepositoryContext>().RefreshAsync();
            await WaitUntilAsync(() => panel.HasUnstaged);

            Assert.True(panel.HasUnstaged);
        });
    }

    // ---------------------------------------------------------------- staging

    [Fact]
    public void Panel_StagesOneFileAndMovesItAcross()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            Write(repository, "README.md", "# edited\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            await panel.StageCommand.ExecuteAsync(Row(panel.Unstaged, "src/app.txt"));

            Assert.Equal(["src/app.txt"], PathsOf(panel.Staged));
            Assert.Equal(["README.md"], PathsOf(panel.Unstaged));
        });
    }

    [Fact]
    public void Panel_StagesAWholeDirectoryFromItsRow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/one.txt", "one\n");
            Write(repository, "src/two.txt", "two\n");
            Write(repository, "README.md", "# edited\n");

            // A directory row is the tree's: the list the panel opens on has none.
            services.Get<ISettingsService>().Update(current => current with { FilesView = FilesView.Tree });

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            ChangedFileNodeViewModel directory = panel.Unstaged.Nodes.Single(node => node.IsDirectory);

            await panel.StageCommand.ExecuteAsync(directory);

            Assert.Equal(["src/one.txt", "src/two.txt"], PathsOf(panel.Staged).Order());
            Assert.Equal(["README.md"], PathsOf(panel.Unstaged));
        });
    }

    [Fact]
    public void Panel_StagesAndUnstagesEverything()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            Write(repository, "notes.txt", "brand new\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            await panel.StageAllCommand.ExecuteAsync(null);

            Assert.Equal(2, PathsOf(panel.Staged).Count);
            Assert.Empty(PathsOf(panel.Unstaged));

            await panel.UnstageAllCommand.ExecuteAsync(null);

            Assert.Empty(PathsOf(panel.Staged));
            Assert.Equal(2, PathsOf(panel.Unstaged).Count);
        });
    }

    [Fact]
    public void Panel_UnstagesOneFile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            await GitAsync(repository, "add", "--all");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            await panel.UnstageCommand.ExecuteAsync(Row(panel.Staged, "src/app.txt"));

            Assert.Empty(PathsOf(panel.Staged));
            Assert.Equal(["src/app.txt"], PathsOf(panel.Unstaged));
        });
    }

    [Fact]
    public void Panel_KeepsThePickedFilePickedAcrossTheHalves()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            Write(repository, "README.md", "# edited\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            Assert.True(panel.Unstaged.SelectPath("src/app.txt"));

            // Staged, the file the reader was looking at is still the one they are looking at — in
            // the half it moved to.
            await panel.StageCommand.ExecuteAsync(Row(panel.Unstaged, "src/app.txt"));

            Assert.Null(panel.Unstaged.SelectedFile);
            Assert.Equal("src/app.txt", panel.Staged.SelectedFile?.Path);
            Assert.Equal("src/app.txt", panel.SelectedChange?.File.Path);
            Assert.Equal(Core.Diff.DiffTarget.Staged(), panel.SelectedChange?.Target);
        });
    }

    // ---------------------------------------------------------------- discarding

    [Fact]
    public void Panel_AsksBeforeDiscardingAndKeepsTheFileOnCancel()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Close;

            await panel.DiscardCommand.ExecuteAsync(Row(panel.Unstaged, "src/app.txt"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("src/app.txt", message, StringComparison.Ordinal);
            Assert.Contains("cannot be undone", message, StringComparison.Ordinal);
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
            Assert.Equal("ruined\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
        });
    }

    [Fact]
    public void Panel_DiscardsOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Primary;

            await panel.DiscardCommand.ExecuteAsync(Row(panel.Unstaged, "src/app.txt"));

            Assert.Equal("one\ntwo\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
            Assert.True(panel.IsClean);
        });
    }

    [Fact]
    public void Panel_AsksOnePlainRedQuestionBeforeDiscardingEverything()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");
            Write(repository, "notes.txt", "never committed\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            bool redWhileAsked = false;
            services.Dialogs.OnShown = dialog => redWhileAsked = dialog.Classes.Contains(ContentDialogServiceExtensions.DangerClass);
            services.Dialogs.Result = DialogResult.Close;

            await panel.DiscardAllCommand.ExecuteAsync(null);

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
    public void Panel_DiscardsEverythingOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");
            Write(repository, "notes.txt", "never committed\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            services.Dialogs.Result = DialogResult.Primary;

            await panel.DiscardAllCommand.ExecuteAsync(null);

            Assert.Equal("one\ntwo\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "notes.txt")));
            Assert.True(panel.IsClean);
        });
    }

    [Fact]
    public void Panel_ConfirmsAOneFileDiscardInRedToo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            bool redWhileAsked = false;
            services.Dialogs.OnShown = dialog => redWhileAsked = dialog.Classes.Contains(ContentDialogServiceExtensions.DangerClass);
            services.Dialogs.Result = DialogResult.Close;

            await panel.DiscardCommand.ExecuteAsync(Row(panel.Unstaged, "src/app.txt"));

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

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);
            (Window window, WorkingTreePanelView view) = Show(panel);

            try
            {
                Button discard = view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "DiscardAll");

                Assert.Contains("danger", discard.Classes);
                Assert.Same(panel.DiscardAllCommand, discard.Command);
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
    public void Panel_WillNotCommitWithoutAMessageOrWithoutAnythingStaged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            // Nothing staged.
            panel.Message = "A fine message";
            Assert.False(panel.CommitCommand.CanExecute(null));

            await panel.StageAllCommand.ExecuteAsync(null);
            Assert.True(panel.CommitCommand.CanExecute(null));

            // Staged, but nothing to say about it.
            panel.Message = "   ";
            Assert.False(panel.CommitCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Panel_CommitsWhatIsStagedAndClearsItself()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);

            await panel.StageAllCommand.ExecuteAsync(null);

            panel.Message = "Change the application file\n\nWith a body that explains why.";

            await panel.CommitCommand.ExecuteAsync(null);

            Assert.Equal("Change the application file", await ReadGitAsync(repository, "log", "-1", "--format=%s"));
            Assert.True(panel.IsClean);
            Assert.Equal(string.Empty, panel.Message);

            // The short hash is what a user recognises the commit by afterwards.
            string sha = await ReadGitAsync(repository, "rev-parse", "--short=7", "HEAD");

            Assert.Contains(services.InfoBar.Shown, note => note.Message.StartsWith(sha, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void CtrlEnter_CommitsFromTheMessageBox()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);
            await panel.StageAllCommand.ExecuteAsync(null);
            panel.Message = "Commit from the keyboard";

            (Window window, WorkingTreePanelView view) = Show(panel);

            try
            {
                TextBox message = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "MessageBox");
                message.Focus();
                Dispatcher.UIThread.RunJobs();

                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
                await WaitUntilAsync(() => panel.IsClean);

                Assert.Equal("Commit from the keyboard", await ReadGitAsync(repository, "log", "-1", "--format=%s"));
            }
            finally
            {
                window.Close();
            }
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

            WorkingTreePanelViewModel panel = await OpenAsync(services, repository);
            (Window window, WorkingTreePanelView view) = Show(panel);

            try
            {
                // Nothing to tick: the box records a new commit and nothing else.
                Assert.Empty(view.GetVisualDescendants().OfType<CheckBox>());

                Button commit = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, panel.CommitCommand));
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
    public void Panel_SaysWhenTheSubjectHasRunOn(string message, bool expected)
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            WorkingTreePanelViewModel panel = services.Get<WorkingTreePanelViewModel>();

            panel.Message = message;

            Assert.Equal(expected, panel.IsSubjectTooLong);
            Assert.Equal(message.Length.ToString(System.Globalization.CultureInfo.CurrentCulture), panel.SubjectLength);
        });
    }

    [Fact]
    public void Panel_SaysWhenABodyLineWillNotWrapWell()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            WorkingTreePanelViewModel panel = services.Get<WorkingTreePanelViewModel>();

            panel.Message = "Subject\n\nShort body line.";

            Assert.False(panel.HasLongBodyLine);

            panel.Message = "Subject\n\n" + new string('x', WorkingTreePanelViewModel.BodyGuide + 1);

            Assert.True(panel.HasLongBodyLine);

            // The subject is measured against its own guide, not the body's.
            Assert.Equal("Subject", panel.Subject);
        });
    }

    // ---------------------------------------------------------------- in the history

    [Fact]
    public void History_TheUncommittedLineOpensTheWorkingTreeInThePanel()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "README.md", "# edited\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = history;

            Window window = new() { Content = view, Width = 1200, Height = 800 };
            window.Show();

            try
            {
                Settle(window);

                Assert.True(history.IsDetailsPanelOpen);
                Assert.True(history.IsWorkingTreeShown);
                Assert.True(history.WorkingTree.IsActive);

                WorkingTreePanelView working = view.GetVisualDescendants().OfType<WorkingTreePanelView>().Single();
                ChangedFilesPanelView files = view.FindControl<ChangedFilesPanelView>("DetailsFiles")!;

                Assert.True(working.IsEffectivelyVisible);
                Assert.False(files.IsEffectivelyVisible);
                Assert.Equal(["README.md"], PathsOf(history.WorkingTree.Unstaged));
                Assert.Equal("Uncommitted changes", view.FindControl<TextBlock>("DetailsSubject")!.Text);

                // Another line: its files, and the working tree put away.
                history.SelectedRow = history.Rows.First(row => row.Commit is not null);
                Settle(window);

                Assert.False(history.IsWorkingTreeShown);
                Assert.False(history.WorkingTree.IsActive);
                Assert.False(working.IsEffectivelyVisible);
                Assert.True(files.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void History_ShowsTheUnstagedDiffOfTheFilePicked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);

            Assert.False(history.IsDiffViewOpen);

            history.WorkingTree.Unstaged.SelectPath("src/app.txt");
            await WaitUntilAsync(() => history.Diff.HasPatch);

            Assert.True(history.IsDiffViewOpen);
            Assert.Equal("src/app.txt", history.Diff.Title);
            Assert.Contains(history.Diff.UnifiedRows, row => row.Single?.Text == "two edited");
        });
    }

    [Fact]
    public void History_ShowsANewUntrackedFileAsEveryLineAdded_NothingOnTheLeft()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/fresh.txt", "first\nsecond\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);

            history.WorkingTree.Unstaged.SelectPath("src/fresh.txt");
            await WaitUntilAsync(() => history.Diff.HasPatch);

            // A diff, as a commit that added the file shows it — not "touches no lines of text".
            Assert.Equal(string.Empty, history.Diff.Message);

            DiffRowViewModel[] lines = [.. history.Diff.SideBySideRows.Where(row => row.Left is not null || row.Right is not null)];

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
    public void History_SwitchesTheDiffToTheStagedHalfWhenThatSideIsPicked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo staged\n");
            await GitAsync(repository, "add", "--all");
            Write(repository, "README.md", "# edited\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);
            WorkingTreePanelViewModel panel = history.WorkingTree;

            panel.Unstaged.SelectPath("README.md");
            await WaitUntilAsync(() => history.Diff.HasPatch);

            Assert.Equal("README.md", history.Diff.Title);

            panel.Staged.SelectPath("src/app.txt");
            await WaitUntilAsync(() => history.Diff.Title == "src/app.txt");

            // Picking on one side clears the other: which half the diff belongs to has to be
            // unambiguous.
            Assert.Null(panel.Unstaged.SelectedFile);
            Assert.Contains(history.Diff.UnifiedRows, row => row.Single?.Text == "two staged");
        });
    }

    [Fact]
    public void History_ShowWhatItChanged_OpensTheWorkingTreesFirstFile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo edited\n");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            // Asked for straight away, before the working tree has been read.
            history.RowCommands.ShowChanges.Execute(history.Rows.Single(row => row.IsUncommitted));
            await WaitUntilAsync(() => history.Diff.HasPatch);

            Assert.True(history.IsDiffViewOpen);
            Assert.Equal("src/app.txt", history.WorkingTree.SelectedChange?.File.Path);
            Assert.Equal("src/app.txt", history.Diff.Title);

            // Back lets go of the file, as it does of a commit's.
            history.CloseDiffViewCommand.Execute(null);

            Assert.Null(history.WorkingTree.SelectedChange);
            Assert.True(history.IsDetailsPanelOpen);
        });
    }

    [Fact]
    public void History_ACommitFromThePanelIsDrawnAtOnce()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            Write(repository, "README.md", "# still to do\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);
            WorkingTreePanelViewModel panel = history.WorkingTree;

            await panel.StageCommand.ExecuteAsync(Row(panel.Unstaged, "src/app.txt"));
            panel.Message = "Change the application file";
            await panel.CommitCommand.ExecuteAsync(null);

            // Not at the next automatic refresh: now.
            await WaitUntilAsync(() => history.Rows.Any(row => row.Subject == "Change the application file"));

            Assert.Contains(history.Rows, row => row.Subject == "Change the application file");

            // What is still uncommitted is still the selected line, and the panel still shows it.
            await WaitUntilAsync(() => history.SelectedRow is { IsUncommitted: true } && panel.HasUnstaged);

            Assert.True(history.SelectedRow?.IsUncommitted);
            Assert.True(history.IsWorkingTreeShown);
            Assert.Equal(["README.md"], PathsOf(panel.Unstaged));
        });
    }

    [Fact]
    public void History_ACommitThatTakesTheOpenFile_PutsItsDiffAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "one\ntwo changed\n");
            await GitAsync(repository, "add", "--all");
            Write(repository, "README.md", "# still to do\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);
            WorkingTreePanelViewModel panel = history.WorkingTree;

            panel.Staged.SelectPath("src/app.txt");
            await WaitUntilAsync(() => history.Diff.HasPatch);
            Assert.True(history.IsDiffViewOpen);

            panel.Message = "Change the application file";
            await panel.CommitCommand.ExecuteAsync(null);

            // The file is in neither list any more: its diff has nothing left to describe, and the
            // graph it covered is where the commit is.
            Assert.False(history.IsDiffViewOpen);
            await WaitUntilAsync(() => history.Rows.Any(row => row.Subject == "Change the application file"));
            Assert.Contains(history.Rows, row => row.Subject == "Change the application file");
        });
    }

    [Fact]
    public void History_ATreeLeftCleanTakesTheLineAndThePanelAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/app.txt", "ruined\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);

            services.Dialogs.Result = DialogResult.Primary;
            await history.WorkingTree.DiscardAllCommand.ExecuteAsync(null);

            // Waiting for the reload to be over, not for its first step: the rows are emptied before
            // they are read again.
            await WaitUntilAsync(() => !history.IsDetailsPanelOpen && history.Rows.Count > 0);

            Assert.DoesNotContain(history.Rows, row => row.IsUncommitted);
            Assert.Null(history.SelectedRow);
            Assert.False(history.IsDetailsPanelOpen);
            Assert.False(history.WorkingTree.IsActive);
        });
    }

    [Fact]
    public void History_TheFirstCommitOfANewRepositoryIsMadeFromThePanel()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "new"), "main");

            Write(repository, "README.md", "# a beginning\n");

            HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);
            WorkingTreePanelViewModel panel = history.WorkingTree;

            Assert.Equal(["README.md"], PathsOf(panel.Unstaged));

            await panel.StageAllCommand.ExecuteAsync(null);
            panel.Message = "Begin";
            await panel.CommitCommand.ExecuteAsync(null);

            Assert.Equal("Begin", await ReadGitAsync(repository, "log", "-1", "--format=%s"));

            await WaitUntilAsync(() => history.Rows.Any(row => row.Subject == "Begin"));

            Assert.Contains(history.Rows, row => row.Subject == "Begin");
            Assert.DoesNotContain(history.Rows, row => row.IsUncommitted);
        });
    }

    [Fact]
    public void History_SaysWhereTheFirstCommitIsMade()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "empty"), "main");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            Assert.True(history.IsEmpty);
            Assert.Contains("uncommitted line", history.EmptyMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("Changes page", history.EmptyMessage, StringComparison.Ordinal);
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void History_DrawsBothHalvesAndTheCommitBoxInThePanel()
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

                HistoryPageViewModel history = await OpenInTheHistoryAsync(services, repository);
                history.WorkingTree.Message = "Describe the change";

                HistoryPageView view = services.Get<HistoryPageView>();
                view.DataContext = history;

                Window window = new() { Content = view, Width = 1280, Height = 760 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "history-working-tree.png");

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
                        ?? throw new InvalidOperationException("The history produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);

                    if (colours >= 8)
                    {
                        break;
                    }
                }

                Assert.True(colours >= 8, "the history drew nothing");

                WorkingTreePanelView working = view.GetVisualDescendants().OfType<WorkingTreePanelView>().Single();

                string[] texts = [.. working.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible)
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("Not staged", texts);
                Assert.Contains("Staged", texts);
                Assert.Contains("app.txt", texts);
                Assert.Contains("README.md", texts);
                Assert.Contains("Commit", working.GetVisualDescendants().OfType<Button>().Select(button => button.Content as string));

                window.Content = null;
                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    private static (Window Window, WorkingTreePanelView View) Show(WorkingTreePanelViewModel panel)
    {
        WorkingTreePanelView view = new() { DataContext = panel };

        Window window = new() { Content = view, Width = 420, Height = 700 };
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }
    }
}
