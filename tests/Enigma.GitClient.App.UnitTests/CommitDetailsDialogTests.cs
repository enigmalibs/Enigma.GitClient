using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.History;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

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
}
