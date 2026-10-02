using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.Navigation;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.Navigation;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The navigation rail of both windows: no wider than its longest label needs, and no label broken
/// across two lines.
/// </summary>
/// <remarks>
/// The labels are laid out in Inter, the face the application draws in. The headless fixture does
/// not register it — doing so would change every other test's text layout — so the windows here are
/// given it explicitly, straight from the font package. A fallback face is wider, and would fail a
/// rail that is right. What is measured is the real layout: the room the library's item actually
/// hands a label, not a sum of its padding and margins written down here.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class NavigationRailTests
{
    // What both rails were before they were narrowed.
    private const double FormerWidth = 96;

    // The room a label keeps to spare, for layout rounding at a fractional scale; and the step the
    // rail's width is taken in.
    private const double Slack = 2;
    private const double Step = 4;

    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    private readonly HeadlessAvaloniaFixture _fixture;

    public NavigationRailTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    public static TheoryData<string> Rails => ["RepositoryRailWidth", "StartRailWidth"];

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        App.ConfigureServices(services);

        services.RemoveAll<IRefReader>();
        services.AddSingleton<IRefReader, FakeRefReader>();

        return services.BuildServiceProvider();
    }

    private static double Resource(string key)
    {
        Assert.True(Application.Current!.TryFindResource(key, out object? width));

        return Assert.IsType<double>(width);
    }

    /// <summary>
    /// The window a rail belongs to, shown and laid out in Inter; the repository window's rail with
    /// the conflicts page in it, which is a label like the others while a merge is stopped.
    /// </summary>
    private static Window Show(ServiceProvider provider, string rail)
    {
        Window window;

        if (rail == "RepositoryRailWidth")
        {
            provider.GetRequiredService<IShellNavigation>().SetConflictsVisible(true);

            window = provider.GetRequiredService<MainWindow>();
            window.DataContext = provider.GetRequiredService<MainWindowViewModel>();
        }
        else
        {
            window = provider.GetRequiredService<StartWindow>();
            window.DataContext = provider.GetRequiredService<StartWindowViewModel>();
        }

        window.FontFamily = Inter;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return window;
    }

    private static NavigationView Rail(Window window)
        => window.GetLogicalDescendants().OfType<NavigationView>().Single();

    private static List<TextBlock> Labels(NavigationView rail)
        => [.. rail.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.FindAncestorOfType<NavigationItem>() is not null)];

    /// <summary>
    /// How much wider the label could be before it no longer fits the room its item gives it.
    /// </summary>
    private static double Spare(TextBlock label)
    {
        Size? room = LayoutInformation.GetPreviousMeasureConstraint(label);
        Assert.NotNull(room);

        return room.Value.Width - label.TextLayout.WidthIncludingTrailingWhitespace;
    }

    [Theory]
    [MemberData(nameof(Rails))]
    public void TheRail_IsItsResourceWide_AndNarrowerThanBefore(string rail)
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            double width = Resource(rail);
            Window window = Show(provider, rail);

            try
            {
                NavigationView view = Rail(window);

                Assert.True(width < FormerWidth, $"{rail} is {width}, no narrower than the former {FormerWidth}");
                Assert.Equal(width, view.PaneSize);
                Assert.Equal(width, view.Bounds.Width, tolerance: 0.5);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(Rails))]
    public void EveryLabel_StaysOnOneLine_WithRoomToSpare(string rail)
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            Window window = Show(provider, rail);

            try
            {
                List<TextBlock> labels = Labels(Rail(window));
                Assert.NotEmpty(labels);

                foreach (TextBlock label in labels)
                {
                    Assert.Equal(Inter, label.FontFamily);
                    Assert.True(
                        label.TextLayout.TextLines.Count == 1,
                        $"'{label.Text}' is broken over {label.TextLayout.TextLines.Count} lines");
                    Assert.True(Spare(label) >= Slack, $"'{label.Text}' has only {Spare(label):0.00} to spare");
                }

                // The longest label of either window, and the one that shows only during a merge.
                Assert.Contains(labels, label => label.Text is "Repositories" or "Conflicts");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(Rails))]
    public void TheRail_IsNoWiderThanItsLongestLabelNeeds(string rail)
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            Window window = Show(provider, rail);

            try
            {
                // One step narrower, the longest label would no longer keep its slack: the width is
                // the least one that does, which is the space the rail was asked to lose.
                double tightest = Labels(Rail(window)).Min(Spare);

                Assert.InRange(tightest, Slack, Slack + Step);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
