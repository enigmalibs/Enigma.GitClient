using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.Core.Diagnostics;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The splash screen: how long it is held, what it shows, and the hand-over that closes it.
/// </summary>
/// <remarks>
/// What the window shows comes from <see cref="ProductInformation"/> through <c>x:Static</c>, so the
/// assertions are that the markup loads, that the asset behind the icon resolves, that the two lines
/// say what the product says, and that the window has the flags a splash depends on. Whether it looks
/// right is a reading of the captured frame, recorded in the completion doc.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class SplashScreenTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public SplashScreenTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(0, 1000, 1000)]
    [InlineData(400, 1000, 600)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1200, 1000, 0)]
    [InlineData(5000, 0, 0)]
    public void RemainingDelay_IsWhatIsLeftOfTheFloor_AndNeverNegative(int elapsedMs, int minimumMs, int expectedMs)
        => Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMs),
            SplashTiming.RemainingDelay(TimeSpan.FromMilliseconds(elapsedMs), TimeSpan.FromMilliseconds(minimumMs)));

    [Fact]
    public void TheFloor_IsOneSecond()
        => Assert.Equal(TimeSpan.FromSeconds(1), SplashTiming.MinimumDisplay);

    [Fact]
    public void TheSplash_ShowsTheProductAndItsVersion()
    {
        _fixture.Run(() =>
        {
            SplashWindow splash = Show();

            try
            {
                string[] texts = [.. splash.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty)];

                Assert.Contains("Enigma git client", texts);
                Assert.Contains("Version", texts);
                Assert.Contains(ProductInformation.GetVersion(), texts);
            }
            finally
            {
                splash.Close();
            }
        });
    }

    [Fact]
    public void TheIcon_IsAResourceThatResolves()
    {
        _fixture.Run(() =>
        {
            SplashWindow splash = Show();

            try
            {
                // A missing asset fails here rather than drawing nothing: the Source converter opens it
                // as the markup is loaded.
                Image icon = Assert.Single(splash.GetLogicalDescendants().OfType<Image>());

                Assert.NotNull(icon.Source);
                Assert.Equal(96, icon.Width);
            }
            finally
            {
                splash.Close();
            }
        });
    }

    [Fact]
    public void TheWindow_IsShapedLikeEnigmaMarkdownEditorsSplash()
    {
        _fixture.Run(() =>
        {
            SplashWindow splash = Show();

            try
            {
                // Undecorated and centred because nobody interacts with it, off the taskbar because it
                // is gone before it could be clicked there, on top because the first window is shown
                // behind it during the hand-over.
                Assert.Equal(420, splash.Width);
                Assert.Equal(260, splash.Height);
                Assert.Equal(WindowDecorations.None, splash.WindowDecorations);
                Assert.Equal(WindowStartupLocation.CenterScreen, splash.WindowStartupLocation);
                Assert.False(splash.ShowInTaskbar);
                Assert.False(splash.CanResize);
                Assert.True(splash.Topmost);
            }
            finally
            {
                splash.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSurfaceAndItsBorder_ResolveInEitherTheme(bool dark)
    {
        _fixture.Run(() =>
        {
            SplashWindow splash = new() { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            splash.Show();

            try
            {
                Border frame = Assert.Single(splash.GetLogicalDescendants().OfType<Border>());

                Assert.NotNull(splash.Background);
                Assert.NotNull(frame.BorderBrush);
            }
            finally
            {
                splash.Close();
            }
        });
    }

    [Fact]
    public void TheSplash_IsDrawn()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            SplashWindow splash = Show();

            try
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "splash.png");

                int colours = 0;

                for (int attempt = 0; attempt < 20 && colours < 8; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    splash.UpdateLayout();
                    splash.InvalidateVisual();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = splash.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The splash produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);
                }

                // The icon's colours on top of the surface, the border and the text: a flat frame is a
                // splash whose theme or asset never arrived.
                Assert.True(colours >= 8, "the splash drew nothing");
            }
            finally
            {
                splash.Close();
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void TheHandOver_HoldsTheSplashUntilItsFloor()
    {
        _fixture.RunAsync(async () =>
        {
            ManualTimeProvider time = new();
            SplashWindow splash = Show();
            SplashHandOver handOver = new(splash, TimeSpan.FromSeconds(1), time);

            try
            {
                time.Advance(TimeSpan.FromMilliseconds(400));
                Task waiting = handOver.WaitForMinimumAsync();

                Assert.False(waiting.IsCompleted);

                time.Advance(TimeSpan.FromMilliseconds(600));
                await waiting.WaitAsync(TimeSpan.FromSeconds(5));

                // A start slower than the floor waits for nothing at all.
                Assert.True(handOver.WaitForMinimumAsync().IsCompleted);
            }
            finally
            {
                handOver.Close();
            }
        });
    }

    [Fact]
    public void TheHandOver_ClosesTheSplashOnce()
    {
        _fixture.Run(() =>
        {
            SplashWindow splash = Show();
            SplashHandOver handOver = new(splash, TimeSpan.Zero, TimeProvider.System);
            int closed = 0;
            splash.Closed += (_, _) => closed++;

            handOver.Close();
            handOver.Close();

            Assert.Equal(1, closed);
            Assert.False(splash.IsVisible);
        });
    }

    private static SplashWindow Show()
    {
        SplashWindow splash = new();
        splash.Show();

        return splash;
    }
}
