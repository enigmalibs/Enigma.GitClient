using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Every dialog opens with the focus where the next keystroke belongs — its first text field, else its
/// default button, Cancel on a red question — and gives it back to where it was as it closes.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DialogFocusTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DialogFocusTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void AForm_OpensWithTheCaretInItsFirstField()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            CreateBranchDialogView view = new()
            {
                DataContext = new CreateBranchDialogViewModel([new BranchStartPoint("HEAD", "HEAD", "now")], []),
            };

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Create a branch";
                dialog.Content = view;
                dialog.PrimaryButtonText = "Create";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            TextBox name = Named<TextBox>(view, "Branch name");
            Assert.True(name.IsFocused, "the name box does not have the focus: the name cannot be typed straight away");
            Assert.False(stage.Opener.IsFocused);

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void AReadOnlyBox_IsNotAField_TheNextOneIs()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            TextBox details = new() { Text = "at Something()", IsReadOnly = true };
            TextBox note = new() { Text = string.Empty };

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Report";
                dialog.Content = new StackPanel { Children = { details, note } };
                dialog.PrimaryButtonText = "Send";
                dialog.CloseButtonText = "Cancel";
            });
            stage.Settle();

            Assert.True(note.IsFocused);
            Assert.False(details.IsFocused);

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void OnlyAReadOnlyBox_TheFocusGoesToTheButton()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            TextBox details = new() { Text = "at Something()", IsReadOnly = true };

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Something went wrong";
                dialog.Content = details;
                dialog.CloseButtonText = "Close";
                dialog.DefaultButton = DefaultButton.Close;
            });
            stage.Settle();

            Assert.False(details.IsFocused);
            Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused);

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void AConfirmation_FocusesItsMainButton_AndEnterConfirms()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Revert abc1234?";
                dialog.Content = "A new commit undoes what it changed.";
                dialog.PrimaryButtonText = "Revert";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.PrimaryButtonPart).IsFocused, "the main button does not have the focus");

            stage.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.Equal(DialogResult.Primary, await asking);
        });
    }

    [Fact]
    public void FromTheMainButton_TabGoesToCancel()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Stash the uncommitted changes";
                dialog.Content = "They can be brought back later.";
                dialog.PrimaryButtonText = "Stash";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            stage.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused, "Tab did not go on to Cancel");

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Theory]
    [InlineData(DefaultButton.None, DialogFocus.PrimaryButtonPart)]
    [InlineData(DefaultButton.Primary, DialogFocus.PrimaryButtonPart)]
    [InlineData(DefaultButton.Secondary, DialogFocus.SecondaryButtonPart)]
    [InlineData(DefaultButton.Close, DialogFocus.CloseButtonPart)]
    public void ADialogsDefaultButton_HasTheFocus(DefaultButton byDefault, string focused)
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "This will detach HEAD";
                dialog.Content = "Commits made from there belong to no branch.";
                dialog.PrimaryButtonText = "Check out anyway";
                dialog.SecondaryButtonText = "Create a branch here…";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = byDefault;
            });
            stage.Settle();

            Assert.True(stage.Button(focused).IsFocused, $"{focused} does not have the focus");

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void AMessage_WithOnlyAClosingButton_FocusesIt()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<DialogResult> asking = stage.Dialogs.ShowMessageAsync("git is required", "Install git 2.20 or newer.");
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused);

            stage.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.Equal(DialogResult.Close, await asking);
        });
    }

    [Fact]
    public void ADisabledMainButton_PassesTheFocusToTheNextShownOne()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Set upstream";
                dialog.Content = "No remote branch to track.";
                dialog.PrimaryButtonText = "Set";
                dialog.IsPrimaryButtonEnabled = false;
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused);

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void ARedQuestion_FocusesCancel_AndAStrayEnterCancels()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<bool> asking = stage.Dialogs.ConfirmDestructiveAsync("Delete branch", "\"topic\" is gone for good.", "Delete");
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused, "Cancel does not have the focus");
            Assert.False(stage.Button(DialogFocus.PrimaryButtonPart).IsFocused);

            stage.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.False(await asking);
        });
    }

    [Fact]
    public void ARedQuestion_FocusesCancel_WhateverItsDefaultButtonSays()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            stage.Host.Classes.Add(ContentDialogServiceExtensions.DangerClass);

            try
            {
                Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
                {
                    dialog.Title = "Remove worktree";
                    dialog.Content = "Its folder is deleted.";
                    dialog.PrimaryButtonText = "Remove";
                    dialog.CloseButtonText = "Cancel";
                    dialog.DefaultButton = DefaultButton.Primary;
                });
                stage.Settle();

                Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused);

                await stage.Host.HideAsync();
                await asking;
            }
            finally
            {
                stage.Host.Classes.Remove(ContentDialogServiceExtensions.DangerClass);
            }
        });
    }

    [Fact]
    public void TheSquashDialog_KeepsTheCaretOnTheMessage_NotInTheBranchName()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            SquashCandidate candidate = new(
                new SquashEnd("b".PadRight(40, '0'), "b000000", "Base"),
                new SquashEnd("t".PadRight(40, '0'), "t000000", "Tip"),
                ["topic"],
                HeadIsAtTip: true);
            SquashPreview preview = new(candidate.Base.Sha, candidate.Tip.Sha, 2, 0, ["One", "Two"], [], 0, ChangesNothing: false);
            SquashDialogView view = new() { DataContext = new SquashDialogViewModel(candidate, preview, ["main", "topic"]) };

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Squash";
                dialog.Content = view;
                dialog.PrimaryButtonText = "Squash";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            TextBox message = Named<TextBox>(view, "Message");
            Assert.True(message.IsFocused, "the message does not have the focus");
            Assert.Equal(0, message.CaretIndex);
            Assert.False(Named<TextBox>(view, "New branch").IsFocused);

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void Closing_GivesTheFocusBackToWhatOpenedTheDialog()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Revert abc1234?";
                dialog.Content = "A new commit undoes what it changed.";
                dialog.PrimaryButtonText = "Revert";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();
            Assert.False(stage.Opener.IsFocused);

            stage.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(DialogResult.None, await asking);
            stage.Settle();

            Assert.True(stage.Opener.IsFocused, "the focus was not given back to what opened the dialog");
            Assert.False(stage.HostButton(DialogFocus.PrimaryButtonPart).IsFocused);
        });
    }

    [Fact]
    public void AQuestionOverAToolDialog_TakesTheFocus_ThenGivesItBackToTheToolDialog()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            // The tool dialog — the branches page, its filter first — under the questions' host.
            TextBox filter = new() { Text = string.Empty };
            Button delete = new() { Content = "Delete the selected branches" };
            stage.Tools.Content = new StackPanel { Children = { filter, delete } };
            stage.Tools.CloseButtonText = "Close";
            stage.Tools.DefaultButton = DefaultButton.Close;
            Task<DialogResult> tool = stage.Tools.ShowAsync();
            stage.Settle();

            Assert.True(filter.IsFocused, "the tool dialog's filter does not have the focus");

            // A question raised from inside it.
            delete.Focus();
            Task<bool> question = stage.Dialogs.ConfirmDestructiveAsync("Delete branch", "Gone for good.", "Delete");
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.CloseButtonPart).IsFocused);

            await stage.Host.HideAsync();
            Assert.False(await question);
            stage.Settle();

            Assert.True(delete.IsFocused, "the focus did not go back into the tool dialog");

            await stage.Tools.HideAsync();
            await tool;
            stage.Settle();

            Assert.True(stage.Opener.IsFocused);
        });
    }

    [Fact]
    public void AttachingTwice_ChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open();

            DialogFocus.Attach(stage.Host);

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Stash the uncommitted changes";
                dialog.Content = "They can be brought back later.";
                dialog.PrimaryButtonText = "Stash";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            Assert.True(stage.Button(DialogFocus.PrimaryButtonPart).IsFocused);

            await stage.Host.HideAsync();
            await asking;
            stage.Settle();

            Assert.True(stage.Opener.IsFocused);
        });
    }

    [Fact]
    public void AHostNotAttached_MovesNoFocus()
    {
        _fixture.RunAsync(async () =>
        {
            using Stage stage = Stage.Open(attach: false);

            Task<DialogResult> asking = stage.Dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Stash the uncommitted changes";
                dialog.Content = "They can be brought back later.";
                dialog.PrimaryButtonText = "Stash";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
            });
            stage.Settle();

            Assert.True(stage.Opener.IsFocused);

            await stage.Host.HideAsync();
            await asking;
        });
    }

    [Fact]
    public void TheWindows_AttachEveryDialogHost()
    {
        // The windows are built without being shown: attaching happens in their constructors.
        _fixture.Run(() =>
        {
            MainWindow main = new();
            StartWindow start = new();

            Assert.True(DialogFocus.IsAttached(((IHostWindow)main).DialogHost));
            Assert.True(DialogFocus.IsAttached(((IToolDialogHostWindow)main).ToolDialogHost));
            Assert.True(DialogFocus.IsAttached(((IHostWindow)start).DialogHost));
        });
    }

    private static T Named<T>(Control view, string name)
        where T : Control
        => view.GetLogicalDescendants()
            .OfType<T>()
            .Single(control => global::Avalonia.Automation.AutomationProperties.GetName(control) == name);

    /// <summary>
    /// A window with a button that has the focus — what opens a dialog — the tool dialogs' host and,
    /// above it, the questions' host, both attached as the windows attach theirs.
    /// </summary>
    private sealed class Stage : IDisposable
    {
        private Stage(Window window, Button opener, ContentDialog tools, ContentDialog host, ContentDialogService dialogs)
        {
            Window = window;
            Opener = opener;
            Tools = tools;
            Host = host;
            Dialogs = dialogs;
        }

        public Window Window { get; }

        public Button Opener { get; }

        public ContentDialog Tools { get; }

        public ContentDialog Host { get; }

        public ContentDialogService Dialogs { get; }

        public static Stage Open(bool attach = true)
        {
            Button opener = new() { Content = "Open" };
            ContentDialog tools = new();
            ContentDialog host = new();
            Window window = new() { Content = new Panel { Children = { opener, tools, host } }, Width = 1200, Height = 800 };
            window.Show();

            ContentDialogService dialogs = new();
            dialogs.RegisterHost(host);

            if (attach)
            {
                DialogFocus.Attach(tools);
                DialogFocus.Attach(host);
            }

            opener.Focus();
            Assert.True(opener.IsFocused);

            return new Stage(window, opener, tools, host, dialogs);
        }

        public Button Button(string part)
            => Host.IsOpen
                ? Part(Host, part)
                : Part(Tools, part);

        public Button HostButton(string part) => Part(Host, part);

        public void Settle()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
            }

            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Window.Close();

        private static Button Part(ContentDialog host, string part)
            => host.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Name == part && ReferenceEquals(button.TemplatedParent, host));
    }
}
