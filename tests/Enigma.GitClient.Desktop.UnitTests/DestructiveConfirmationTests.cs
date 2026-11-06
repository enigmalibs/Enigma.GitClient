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

    [Fact]
    public void TheHarmlessButton_KeepsTheLabelItIsGiven()
    {
        _fixture.RunAsync(async () =>
        {
            ScriptedContentDialogService dialogs = new() { Result = DialogResult.Close };

            Assert.False(await dialogs.ConfirmDestructiveAsync("Delete this profile", "Gone.", "Delete", "Keep it"));

            ContentDialog dialog = Assert.Single(dialogs.Shown);
            Assert.Equal("Delete", dialog.PrimaryButtonText);
            Assert.Equal("Keep it", dialog.CloseButtonText);
            Assert.Equal(DefaultButton.Close, dialog.DefaultButton);
        });
    }

    [Fact]
    public void ARedQuestion_HasCancelAsItsDefault_WhateverItAsksFor()
    {
        _fixture.RunAsync(async () =>
        {
            ScriptedContentDialogService dialogs = new() { Result = DialogResult.Secondary };
            bool redWhileAsked = false;
            dialogs.OnShown = dialog => redWhileAsked = dialog.Classes.Contains(ContentDialogServiceExtensions.DangerClass);

            DialogResult answer = await dialogs.ShowDestructiveAsync(dialog =>
            {
                dialog.Title = "Reset \"topic\" to \"origin/topic\"?";
                dialog.Content = "These commits are only on \"topic\", and are left behind.";
                dialog.PrimaryButtonText = "Reset local to here";
                dialog.SecondaryButtonText = "Check out \"topic\"";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });

            // Every answer comes back, the secondary one included.
            Assert.Equal(DialogResult.Secondary, answer);

            ContentDialog asked = Assert.Single(dialogs.Shown);
            Assert.True(redWhileAsked);
            Assert.Equal(DefaultButton.Close, asked.DefaultButton);
            Assert.Equal("Check out \"topic\"", asked.SecondaryButtonText);
            Assert.DoesNotContain(ContentDialogServiceExtensions.DangerClass, asked.Classes);
        });
    }

    [Fact]
    public void ALongRedQuestion_ScrollsInABoundedCard_AndTheHeightIsGivenBack()
    {
        _fixture.RunAsync(async () =>
        {
            ScriptedContentDialogService dialogs = new() { Result = DialogResult.Close };
            double heightWhileAsked = double.NaN;
            dialogs.OnShown = dialog => heightWhileAsked = dialog.DialogMaxHeight;

            string message = string.Join('\n', Enumerable.Range(1, 30).Select(line => $"branch-{line}"));
            await dialogs.ShowDestructiveAsync(dialog =>
            {
                dialog.Title = "Delete 30 branches";
                dialog.Content = message;
                dialog.PrimaryButtonText = "Delete";
                dialog.CloseButtonText = "Cancel";
            });

            Assert.Equal(ContentDialogServiceExtensions.LongQuestionHeight, heightWhileAsked);
            Assert.Equal(double.PositiveInfinity, Assert.Single(dialogs.Shown).DialogMaxHeight);
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
