using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Formatting;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The commit details dialog: what it says about a commit, and that all of it is text to select, not
/// a field to edit.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CommitDetailsDialogTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly HeadlessAvaloniaFixture _fixture;

    public CommitDetailsDialogTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static GitCommit Commit(string body = "Explain why, in more than one line.\nThe second one.\n")
    {
        GitSignature author = new("Ada Lovelace", "ada@example.com", Now.AddHours(-3));
        GitSignature committer = new("Charles Babbage", "charles@example.com", Now.AddHours(-1));

        return new GitCommit(
            "0123456789abcdef0123456789abcdef01234567",
            [],
            author,
            committer,
            "Teach the engine to count",
            body);
    }

    [Fact]
    public void TheViewModel_SaysWhatTheCommitIsWhoWroteItAndWhen()
    {
        CommitDetailsViewModel details = new(Commit(), Now);

        Assert.Equal("Teach the engine to count", details.Subject);
        Assert.Equal("Explain why, in more than one line.\nThe second one.", details.Body);
        Assert.True(details.HasBody);

        // The author, not the committer: the history's date column is the author's too.
        Assert.Equal("Ada Lovelace <ada@example.com>", details.Author);
        Assert.Equal(RelativeTime.FormatAbsolute(Now.AddHours(-3)), details.Date);
        Assert.Equal("3 hours ago", details.Age);
        Assert.Equal($"{details.Date} (3 hours ago)", details.DateWithAge);

        Assert.Equal("0123456789abcdef0123456789abcdef01234567", details.Sha);
    }

    [Fact]
    public void TheViewModel_HasNoDescriptionForACommitWithout()
    {
        CommitDetailsViewModel details = new(Commit(body: string.Empty), Now);

        Assert.Equal(string.Empty, details.Body);
        Assert.False(details.HasBody);
    }

    [Fact]
    public void TheService_ShowsTheViewWithOneButtonThatOnlyCloses()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            await services.Get<ICommitDetailsDialogService>().ShowAsync(Commit());

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Commit details", dialog.Title);
            Assert.NotNull(dialog.IconData);
            Assert.Equal("Close", dialog.CloseButtonText);
            Assert.Null(dialog.PrimaryButtonText);
            Assert.Null(dialog.SecondaryButtonText);

            CommitDetailsView view = Assert.IsType<CommitDetailsView>(dialog.Content);
            CommitDetailsViewModel details = Assert.IsType<CommitDetailsViewModel>(view.DataContext);

            // Measured from the application's clock.
            Assert.Equal("3 hours ago", details.Age);
        });
    }

    [Fact]
    public void TheView_ShowsEveryValueAsSelectableText_AndNoField()
    {
        _fixture.Run(() =>
        {
            CommitDetailsView view = new() { DataContext = new CommitDetailsViewModel(Commit(), Now) };
            Window window = new() { Content = view, Width = 700, Height = 500 };
            window.Show();

            try
            {
                SelectableTextBlock Value(string name)
                    => view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(block => block.Name == name);

                Assert.Equal("Teach the engine to count", Value("Subject").Text);
                Assert.Equal("Explain why, in more than one line.\nThe second one.", Value("Body").Text);
                Assert.True(Value("Body").IsVisible);
                Assert.Equal("Ada Lovelace <ada@example.com>", Value("Author").Text);
                Assert.EndsWith("(3 hours ago)", Value("Date").Text, StringComparison.Ordinal);
                Assert.Equal("0123456789abcdef0123456789abcdef01234567", Value("Sha").Text);

                // Text to select, never a box to type in.
                Assert.Empty(view.GetVisualDescendants().OfType<TextBox>());

                // The labels are plain text, so a drag over a value selects the value alone.
                string[] labels = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => block is not SelectableTextBlock && block.Classes.Contains("label"))
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Equal(["Author", "Date", "Commit"], labels);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheView_HidesTheDescriptionOfACommitWithout()
    {
        _fixture.Run(() =>
        {
            CommitDetailsView view = new() { DataContext = new CommitDetailsViewModel(Commit(body: string.Empty), Now) };
            Window window = new() { Content = view };
            window.Show();

            try
            {
                SelectableTextBlock body = view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(block => block.Name == "Body");
                Assert.False(body.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- from a line's menu

    [Fact]
    public void ALinesMenu_OffersTheDetailsRightAfterTheChanges_OnCommitsAndStashesOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel page = await OpenAsync(services);

            static string[] Headers(CommitRowViewModel row)
                => [.. row.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

            CommitRowViewModel commit = page.Rows.First(row => row.Commit is not null && !row.IsStash);
            CommitRowViewModel stash = Assert.Single(page.Rows, row => row.IsStash);
            CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);

            Assert.Equal(["Show what it changed", "Show commit details"], Headers(commit).Take(2));
            Assert.Equal(["Show what it changed", "Show commit details"], Headers(stash).Take(2));
            Assert.DoesNotContain("Show commit details", Headers(uncommitted));

            HistoryMenuEntry entry = commit.MenuEntries.Single(candidate => candidate.Header == "Show commit details");
            Assert.Equal(PhosphorIcon.Article, entry.Icon);
        });
    }

    [Fact]
    public void ALinesMenu_ShowsThatLinesCommit_WhicheverLineIsSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel page = await OpenAsync(services);

            CommitRowViewModel[] commits = [.. page.Rows.Where(row => row.Commit is not null && !row.IsStash)];
            page.SelectedRow = commits[0];

            // The reader right-clicks another line than the one selected.
            CommitRowViewModel clicked = commits[1];
            HistoryMenuEntry entry = clicked.MenuEntries.Single(candidate => candidate.Header == "Show commit details");

            Assert.True(entry.Command!.CanExecute(entry.Parameter));
            await ((IAsyncRelayCommand)entry.Command).ExecuteAsync(entry.Parameter);

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            CommitDetailsViewModel details = Assert.IsType<CommitDetailsViewModel>(Assert.IsType<CommitDetailsView>(dialog.Content).DataContext);

            Assert.Equal(clicked.Sha, details.Sha);
            Assert.Equal("Add the readme", details.Subject);
            Assert.Equal("Ada Lovelace <ada@example.com>", details.Author);
        });
    }

    /// <summary>
    /// Two commits, a stash on top of them, and uncommitted work: every kind of line the history has.
    /// </summary>
    private static async Task<HistoryPageViewModel> OpenAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "details"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# two\n");
        Git(repository, "commit", "-a", "-m", "Say it twice", "-m", "Because once was not enough.");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# stashed\n");
        Git(repository, "stash", "push", "-m", "Work");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# uncommitted\n");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return page;
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
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

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }
}
