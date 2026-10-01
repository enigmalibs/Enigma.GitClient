using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A question whose answer throws work away: a plain yes or no, its confirm button red while it is
/// asked, and the window's next question in its usual colours again.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DestructiveConfirmationTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DestructiveConfirmationTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(DialogResult.Primary, true)]
    [InlineData(DialogResult.Close, false)]
    [InlineData(DialogResult.None, false)]
    [InlineData(DialogResult.Secondary, false)]
    public void OnlyTheConfirmButton_Confirms(DialogResult answer, bool confirmed)
    {
        _fixture.RunAsync(async () =>
        {
            ScriptedContentDialogService dialogs = new() { Result = answer };

            Assert.Equal(confirmed, await dialogs.ConfirmDestructiveAsync("Discard", "Gone for good.", "Discard"));

            ContentDialog dialog = Assert.Single(dialogs.Shown);
            Assert.Equal("Discard", dialog.Title);
            Assert.Equal("Gone for good.", dialog.Content);
            Assert.Equal("Discard", dialog.PrimaryButtonText);
            Assert.Equal("Cancel", dialog.CloseButtonText);
            Assert.Null(dialog.SecondaryButtonText);
            Assert.Equal(DefaultButton.Close, dialog.DefaultButton);
        });
    }

    [Fact]
    public void TheConfirmButtonIsRed_AndTheNextQuestionIsNot()
    {
        _fixture.RunAsync(async () =>
        {
            ContentDialog host = new();
            Window window = new() { Content = new Panel { Children = { new Border(), host } }, Width = 900, Height = 600 };
            window.Show();

            try
            {
                ContentDialogService dialogs = new();
                dialogs.RegisterHost(host);

                Task<bool> asking = dialogs.ConfirmDestructiveAsync("Discard everything", "Gone for good.", "Discard everything");
                Settle(window);

                Assert.True(host.IsOpen);
                Assert.Contains(ContentDialogServiceExtensions.DangerClass, host.Classes);
                Assert.Same(Brush("ActionDangerBrush"), ConfirmButtonPlate(host));
                Assert.Same(Brush("ActionDangerForegroundBrush"), ConfirmButton(host).Foreground);

                // Escape, or a click beside the card: no answer, so no discard — and the red goes.
                await host.HideAsync();
                Assert.False(await asking);
                Assert.DoesNotContain(ContentDialogServiceExtensions.DangerClass, host.Classes);

                // The window's next question is an ordinary one, in the accent blue.
                Task<DialogResult> next = dialogs.ShowAsync(dialog =>
                {
                    dialog.Title = "Stash the uncommitted changes";
                    dialog.PrimaryButtonText = "Stash";
                    dialog.CloseButtonText = "Cancel";
                });
                Settle(window);

                Assert.Same(Brush("ActionAccentBrush"), ConfirmButtonPlate(host));

                await host.HideAsync();
                await next;
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Button ConfirmButton(ContentDialog host)
        => host.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_PrimaryButton");

    private static IBrush? ConfirmButtonPlate(ContentDialog host)
        => ConfirmButton(host).GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(presenter => presenter.Name == "PART_ContentPresenter")
            .Background;

    private static IBrush Brush(string key)
    {
        Application application = Application.Current!;

        Assert.True(application.TryFindResource(key, application.ActualThemeVariant, out object? brush), key);

        return Assert.IsAssignableFrom<IBrush>(brush);
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
