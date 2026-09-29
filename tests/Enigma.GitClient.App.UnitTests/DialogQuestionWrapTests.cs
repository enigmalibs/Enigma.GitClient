using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// A question asked as plain text goes onto as many lines as it needs, and is never cut at the
/// dialog's edge.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DialogQuestionWrapTests
{
    // The discard question for twelve files, as the history's uncommitted line asks it.
    private const string DiscardQuestion =
        "Throw away every uncommitted change — 12 files, staged or not, untracked files included? This cannot be undone.";

    private readonly HeadlessAvaloniaFixture _fixture;

    public DialogQuestionWrapTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheDiscardQuestion_WrapsInsideTheCard()
    {
        _fixture.RunAsync(async () =>
        {
            (Window window, ContentDialog host, ContentDialogService dialogs) = ShowHost(maxWidth: 720);

            try
            {
                Task<bool> asking = dialogs.ConfirmDestructiveAsync("Discard uncommitted files", DiscardQuestion, "Discard");
                Settle(window);

                AssertShownInFull(host, DiscardQuestion, expectLines: 2);

                await host.HideAsync();
                Assert.False(await asking);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnyPlainTextQuestion_Wraps_AWordWithNoBreakInItIncluded()
    {
        _fixture.RunAsync(async () =>
        {
            (Window window, ContentDialog host, ContentDialogService dialogs) = ShowHost(maxWidth: 600);

            // The tag delete asks through the plain ShowAsync, and a tag's name has no space to
            // break at.
            string name = "release/" + new string('x', 120);
            string question = $"Delete the tag \"{name}\"? The commit it points at is not affected.";

            try
            {
                Task<DialogResult> asking = dialogs.ShowAsync(dialog =>
                {
                    dialog.Title = "Delete tag";
                    dialog.Content = question;
                    dialog.PrimaryButtonText = "Delete";
                    dialog.CloseButtonText = "Cancel";
                });
                Settle(window);

                AssertShownInFull(host, question, expectLines: 3);

                await host.HideAsync();
                await asking;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AShortQuestion_StaysOnOneLine()
    {
        _fixture.RunAsync(async () =>
        {
            (Window window, ContentDialog host, ContentDialogService dialogs) = ShowHost(maxWidth: 720);

            try
            {
                Task<bool> asking = dialogs.ConfirmDestructiveAsync("Discard", "Gone for good.", "Discard");
                Settle(window);

                TextBlock text = QuestionText(host, "Gone for good.");
                Assert.Equal(1, LineCount(text));

                await host.HideAsync();
                await asking;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AViewOfItsOwn_KeepsItsOwnTextLayout()
    {
        _fixture.RunAsync(async () =>
        {
            (Window window, ContentDialog host, ContentDialogService dialogs) = ShowHost(maxWidth: 720);

            // A form lays out its own text; the wrapping is only for the text block the presenter makes
            // for a string, and does not reach into a view's own.
            TextBlock label = new() { Text = "Tag name", TextTrimming = TextTrimming.CharacterEllipsis };
            StackPanel form = new() { Children = { label } };

            try
            {
                Task<DialogResult> asking = dialogs.ShowAsync(dialog =>
                {
                    dialog.Title = "Create a tag";
                    dialog.Content = form;
                    dialog.PrimaryButtonText = "Create";
                });
                Settle(window);

                // The view itself is shown, not the line of text that stands for an object nothing
                // knows how to draw: its type name.
                Visual[] shown = [.. host.GetVisualDescendants()];
                Assert.Contains(form, shown);
                Assert.Contains(label, shown);
                Assert.DoesNotContain(shown.OfType<TextBlock>(), text => text.Text == form.ToString());

                Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);

                await host.HideAsync();
                await asking;
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (Window Window, ContentDialog Host, ContentDialogService Dialogs) ShowHost(double maxWidth)
    {
        ContentDialog host = new() { DialogMinWidth = 380, DialogMaxWidth = maxWidth };
        Window window = new() { Content = new Panel { Children = { new Border(), host } }, Width = 1200, Height = 800 };
        window.Show();

        ContentDialogService dialogs = new();
        dialogs.RegisterHost(host);

        return (window, host, dialogs);
    }

    /// <summary>
    /// Asserts the question is laid out whole: wrapped, untrimmed, on at least that many lines, and no
    /// wider than the card that holds it.
    /// </summary>
    private static void AssertShownInFull(ContentDialog host, string question, int expectLines)
    {
        TextBlock text = QuestionText(host, question);

        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.Equal(TextTrimming.None, text.TextTrimming);

        int lines = LineCount(text);
        Assert.True(lines >= expectLines, $"the question is laid out on {lines} line(s)");

        // Every line of it fits: the laid-out text is no wider than the block, and the block no wider
        // than the dialog allows. The width without the trailing spaces, which a wrapped line keeps
        // at its end and does not draw.
        Assert.True(
            text.TextLayout.Width <= text.Bounds.Width + 0.5,
            $"the text is {text.TextLayout.Width} wide in a block {text.Bounds.Width} wide");
        Assert.True(
            text.Bounds.Width <= host.DialogMaxWidth,
            $"the text is {text.Bounds.Width} wide in a dialog at most {host.DialogMaxWidth} wide");
    }

    private static TextBlock QuestionText(ContentDialog host, string question)
        => host.GetVisualDescendants()
            .OfType<TextBlock>()
            .SingleOrDefault(text => string.Equals(text.Text, question, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The dialog does not show the question as a text block.");

    private static int LineCount(TextBlock text) => text.TextLayout.TextLines.Count;

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
