using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A context menu item that cannot be chosen is grey all over: its glyph takes the grey its title
/// takes, in both themes, on the History's line and badge menus as everywhere (BUG-553F).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DisabledMenuIconTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DisabledMenuIconTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The colour an item's title is drawn in, as the theme paints it.
    /// </summary>
    private static Color TitleColourOf(MenuItem item)
    {
        ContentPresenter header = item.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(presenter => presenter.Name == "PART_HeaderPresenter");

        return Assert.IsAssignableFrom<ISolidColorBrush>(header.Foreground).Color;
    }

    private static Color GlyphColourOf(MenuItem item)
        => Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Icon>(item.Icon).Foreground).Color;

    /// <summary>
    /// Opens every menu of a kind on the page, as a right-click would, and reads each shown item's
    /// title and glyph colours, with whether it can be chosen.
    /// </summary>
    private static List<(string Header, bool Enabled, Color Title, Color Glyph)> ReadMenus<TOwner>(HistoryPageView view, Window window)
        where TOwner : Control
    {
        List<(string, bool, Color, Color)> read = [];

        foreach (TOwner owner in view.GetVisualDescendants().OfType<TOwner>().Where(control => control.ContextMenu is not null).ToList())
        {
            ContextMenu menu = owner.ContextMenu!;
            owner.RaiseEvent(new ContextRequestedEventArgs());
            menu.Open(owner);
            Render(window);

            foreach (MenuItem item in menu.GetLogicalDescendants().OfType<MenuItem>().Where(item => item.IsVisible && item.Icon is Icon { IsVisible: true }))
            {
                read.Add(((string)item.Header!, item.IsEffectivelyEnabled, TitleColourOf(item), GlyphColourOf(item)));
            }

            menu.Close();
        }

        return read;
    }

    [Fact]
    public void ADisabledItemsGlyph_IsTheGreyOfItsTitle_OnTheLinesAndTheBadgesMenus_InBothThemes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await BuildRepositoryAsync(services);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            Window window = Show(view);

            try
            {
                foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
                {
                    application.RequestedThemeVariant = variant;
                    Render(window);

                    // A line's menu: resetting the checked-out branch to its own tip cannot be done.
                    // A badge's: the checked-out branch can be neither checked out nor deleted.
                    var lines = ReadMenus<Grid>(view, window);
                    var badges = ReadMenus<RefBadge>(view, window);

                    Assert.Contains(lines, item => !item.Enabled);
                    Assert.Contains(lines, item => item.Enabled);
                    Assert.Contains(badges, item => !item.Enabled);
                    Assert.Contains(badges, item => item.Enabled);

                    foreach ((string header, bool enabled, Color title, Color glyph) in lines.Concat(badges))
                    {
                        // Grey with its title when it cannot be chosen; in its title's colour when it can.
                        Assert.True(title == glyph, $"{variant}: \"{header}\" (enabled: {enabled}) has a {glyph} glyph beside a {title} title");
                    }

                    Color disabled = lines.First(item => !item.Enabled).Title;
                    Assert.All(lines.Concat(badges).Where(item => item.Enabled), item => Assert.NotEqual(disabled, item.Glyph));
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
                window.Close();
            }
        });
    }

    [Fact]
    public void AnyMenusItem_GreysItsGlyphWhenItIsDisabled_AndTakesItsColourBack()
    {
        _fixture.Run(() =>
        {
            MenuItem item = new()
            {
                Header = "Something",
                Icon = new Icon { Kind = PhosphorIcon.Copy, Classes = { "menu" } },
            };
            Border owner = new() { Width = 100, Height = 100, Background = Brushes.Transparent };
            ContextMenu menu = new() { Items = { item } };
            owner.ContextMenu = menu;

            Window window = Show(owner);

            try
            {
                menu.Open(owner);
                Render(window);

                Color enabled = GlyphColourOf(item);
                Assert.Equal(TitleColourOf(item), enabled);

                item.IsEnabled = false;
                Render(window);

                Assert.Equal(TitleColourOf(item), GlyphColourOf(item));
                Assert.NotEqual(enabled, GlyphColourOf(item));

                item.IsEnabled = true;
                Render(window);

                Assert.Equal(enabled, GlyphColourOf(item));

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static Window Show(Control view)
    {
        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Render(window);

        return window;
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "icons"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");
        Git(repository, "tag", "v1.0");
        Git(repository, "branch", "feature");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "app.txt"), "two\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the application");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        return repository;
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
