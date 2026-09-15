using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Renders the real window off-screen and asserts that pixels actually came out.
/// </summary>
/// <remarks>
/// The layout tests prove the tree measures; only a rendered frame proves it was <em>drawn</em>. A
/// window that lays out perfectly and paints nothing — a missing control theme, a transparent
/// background, a template that never applied — looks identical to a correct one from every other
/// angle. The captured PNGs are written next to the test assembly so they can be looked at when
/// something is off.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class ShellSnapshotTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ShellSnapshotTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string SnapshotDirectory
    {
        get
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        App.ConfigureServices(services);
        services.RemoveAll<IRefReader>();
        services.AddSingleton<IRefReader, FakeRefReader>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// What a captured frame actually contains.
    /// </summary>
    /// <param name="PaintedFraction">The share of pixels that are neither transparent nor black.</param>
    /// <param name="DistinctColours">
    /// How many distinct colours the frame holds, quantised to 4 bits per channel. This is the
    /// assertion that matters: a frame that is one flat colour — all black, all white, all
    /// transparent — is what a window that laid out but never drew looks like, and a coverage
    /// measure alone happily passes a blank white page.
    /// </param>
    private readonly record struct FrameContents(double PaintedFraction, int DistinctColours);

    /// <summary>
    /// Shows a window, renders it, saves the frame and measures what came out.
    /// </summary>
    /// <remarks>
    /// The headless renderer produces frames on its own timer, so the first capture after
    /// <c>Show</c> is routinely still blank. The loop below drives the timer and re-captures until
    /// the frame holds real content, which is what makes the snapshot deterministic rather than a
    /// race the test usually wins.
    /// </remarks>
    private static FrameContents RenderAndMeasure(Window window, string fileName)
    {
        window.Width = 1280;
        window.Height = 800;
        window.Show();

        string path = Path.Combine(SnapshotDirectory, fileName);
        FrameContents best = default;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            window.UpdateLayout();
            window.InvalidateVisual();

            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();

            using Bitmap frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The window produced no rendered frame.");

            frame.Save(path, PngBitmapEncoderOptions.Default);

            FrameContents contents = Measure(path);

            if (contents.DistinctColours > best.DistinctColours)
            {
                best = contents;
            }

            if (contents.DistinctColours >= 8)
            {
                break;
            }
        }

        window.Close();

        return best;
    }

    private static FrameContents Measure(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap writeable = WriteableBitmap.Decode(stream);
        using ILockedFramebuffer buffer = writeable.Lock();

        HashSet<int> colours = [];
        int painted = 0;
        int total = 0;

        unsafe
        {
            byte* pixels = (byte*)buffer.Address;

            for (int y = 0; y < buffer.Size.Height; y++)
            {
                byte* row = pixels + (y * buffer.RowBytes);

                for (int x = 0; x < buffer.Size.Width; x++)
                {
                    byte blue = row[(x * 4) + 0];
                    byte green = row[(x * 4) + 1];
                    byte red = row[(x * 4) + 2];
                    byte alpha = row[(x * 4) + 3];

                    total++;

                    if (alpha > 0 && (red > 8 || green > 8 || blue > 8))
                    {
                        painted++;
                    }

                    colours.Add(((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4));
                }
            }
        }

        return new FrameContents(total == 0 ? 0 : (double)painted / total, colours.Count);
    }

    private static void AssertLooksLikeTheShell(FrameContents frame, string what)
    {
        Assert.True(frame.PaintedFraction > 0.05, $"{what}: only {frame.PaintedFraction:P1} of the frame was painted");
        Assert.True(
            frame.DistinctColours >= 8,
            $"{what}: the frame holds only {frame.DistinctColours.ToString(System.Globalization.CultureInfo.InvariantCulture)} distinct colours, "
            + "which is what a window that laid out but never drew looks like");
    }

    /// <summary>
    /// Runs work with the application pinned to a theme variant, restoring whatever was there
    /// before. A snapshot that inherits the ambient variant is not a deterministic snapshot: with
    /// <c>Default</c> the host's own theme decides which brushes resolve.
    /// </summary>
    private static void WithVariant(ThemeVariant variant, Action work)
    {
        Application application = Application.Current!;
        ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

        try
        {
            application.RequestedThemeVariant = variant;
            work();
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    }

    [Fact]
    public void MainWindow_ActuallyPaintsPixels()
    {
        _fixture.Run(() => WithVariant(ThemeVariant.Dark, () =>
        {
            using ServiceProvider provider = BuildProvider();

            MainWindow window = provider.GetRequiredService<MainWindow>();
            window.DataContext = provider.GetRequiredService<MainWindowViewModel>();

            AssertLooksLikeTheShell(RenderAndMeasure(window, "main-window-dark.png"), "dark");
        }));
    }

    [Fact]
    public void MainWindow_PaintsInBothThemeVariants()
    {
        _fixture.Run(() =>
        {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                WithVariant(variant, () =>
                {
                    using ServiceProvider provider = BuildProvider();
                    MainWindow window = provider.GetRequiredService<MainWindow>();
                    window.DataContext = provider.GetRequiredService<MainWindowViewModel>();

                    AssertLooksLikeTheShell(
                        RenderAndMeasure(window, $"main-window-{variant.ToString()!.ToLowerInvariant()}.png"),
                        variant.ToString()!);
                });
            }
        });
    }

    [Fact]
    public void MainWindow_PaintsTheRepositoryStripOnceARepositoryIsOpen()
    {
        _fixture.Run(() => WithVariant(ThemeVariant.Dark, () =>
        {
            using ServiceProvider provider = BuildProvider();

            IRepositoryContext context = provider.GetRequiredService<IRepositoryContext>();
            FakeRefReader reader = (FakeRefReader)provider.GetRequiredService<IRefReader>();
            reader.Head = new HeadState(false, false, "main", "abcdef1234567890", RepositoryOperation.Merge);

            MainWindow window = provider.GetRequiredService<MainWindow>();
            window.DataContext = provider.GetRequiredService<MainWindowViewModel>();

            context.OpenAsync(OperatingSystem.IsWindows()
                    ? new RepositoryHandle(@"C:\src\enigma-gitclient", @"C:\src\enigma-gitclient\.git")
                    : new RepositoryHandle("/src/enigma-gitclient", "/src/enigma-gitclient/.git"))
                .GetAwaiter()
                .GetResult();

            AssertLooksLikeTheShell(
                RenderAndMeasure(window, "main-window-repository-open.png"),
                "repository open");
        }));
    }
}
