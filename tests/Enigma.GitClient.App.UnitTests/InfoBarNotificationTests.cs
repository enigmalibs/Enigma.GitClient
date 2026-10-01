using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The one way the application reports something: a success or an informational message closes
/// itself, a warning or an error stays, and nothing waits for either to close.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class InfoBarNotificationTests
{
    private static readonly TimeSpan TwoAndAHalfSeconds = TimeSpan.FromSeconds(2.5);

    private readonly HeadlessAvaloniaFixture _fixture;

    public InfoBarNotificationTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(InfoBarSeverity.Success, true)]
    [InlineData(InfoBarSeverity.Info, true)]
    [InlineData(InfoBarSeverity.Warning, false)]
    [InlineData(InfoBarSeverity.Error, false)]
    public void Notify_TimesSuccessAndInfo_AndLeavesWarningsAndErrorsUntilClosed(InfoBarSeverity severity, bool timed)
    {
        _fixture.Run(() =>
        {
            RecordingInfoBarService infoBar = new();

            infoBar.Notify("Pushed", "Everything \"main\" had is on origin.", severity);

            RecordedNotification note = Assert.Single(infoBar.Shown);
            Assert.Equal("Pushed", note.Title);
            Assert.Equal("Everything \"main\" had is on origin.", note.Message);
            Assert.Equal(severity, note.Severity);
            Assert.Equal(timed ? TwoAndAHalfSeconds : null, note.DisplayDuration);
        });
    }

    [Fact]
    public void Notify_ReturnsWhileTheBarIsStillOpen()
    {
        _fixture.Run(() =>
        {
            RecordingInfoBarService infoBar = new() { HoldsOpen = true };

            // A bar that stays until it is closed — the real one's behaviour — and the call is back.
            infoBar.Notify("Could not push", "The remote refused it.", InfoBarSeverity.Error);

            Assert.True(infoBar.IsOpen);
        });
    }

    [Fact]
    public void Notify_OnTheRealBar_OpensItTimedOrUntilClosed()
    {
        _fixture.Run(() =>
        {
            InfoBar host = new();
            Window window = new() { Content = host, Width = 600, Height = 200 };
            window.Show();

            InfoBarService infoBar = new();
            infoBar.RegisterHost(host);

            try
            {
                infoBar.Notify("Merged", "\"theirs\" is in \"main\".", InfoBarSeverity.Success);

                Assert.True(host.IsOpen);
                Assert.Equal("Merged", host.Title);
                Assert.Equal(TwoAndAHalfSeconds, host.DisplayDuration);

                // An error arriving on the open bar replaces the message and must not inherit its
                // countdown: it stays until the user has read it.
                infoBar.Notify("Could not merge", "git refused.", InfoBarSeverity.Error);

                Assert.True(host.IsOpen);
                Assert.Equal("Could not merge", host.Title);
                Assert.Equal(InfoBarSeverity.Error, host.Severity);
                Assert.Null(host.DisplayDuration);
            }
            finally
            {
                host.Close();
                window.Close();
            }
        });
    }

    [Fact]
    public void TheRealBar_PaintsItsMessageWithTheLibrarysMessageBrush_InBothThemes()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            InfoBar host = new();
            Window window = new() { Content = host, Width = 600, Height = 200 };
            window.Show();

            InfoBarService infoBar = new();
            infoBar.RegisterHost(host);

            try
            {
                infoBar.Notify("Pushed", "Everything \"main\" had is on origin.", InfoBarSeverity.Info);

                // The message brush is Enigma.Avalonia.Desktop 1.2.0's, made for its softer fills:
                // the dictionary the app merges is that one, and nothing in the app shadows it.
                foreach (ThemeVariant variant in (ThemeVariant[])[ThemeVariant.Dark, ThemeVariant.Light])
                {
                    application.RequestedThemeVariant = variant;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();

                    Assert.True(
                        window.TryFindResource("EnigmaInfoBarMessageForegroundBrush", window.ActualThemeVariant, out object? brush),
                        $"the {variant} theme has no info bar message brush");

                    TextBlock message = host.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Single(text => text.Text == "Everything \"main\" had is on origin.");

                    Assert.Equal(
                        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color,
                        Assert.IsAssignableFrom<ISolidColorBrush>(message.Foreground).Color);
                }
            }
            finally
            {
                host.Close();
                window.Close();
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Notify_BeforeAHostIsRegistered_SaysSo()
    {
        _fixture.Run(() =>
        {
            InfoBarService infoBar = new();

            // A wiring mistake stays loud: it is thrown here, not left in a task nobody looks at.
            Assert.Throws<InvalidOperationException>(
                () => infoBar.Notify("Merged", "\"theirs\" is in \"main\".", InfoBarSeverity.Success));
        });
    }
}
