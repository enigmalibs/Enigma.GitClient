using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A repository tile's menu on the start window: what its buttons do, the path to copy and the tile's
/// icon, each with its glyph, opened by a right-click or the keyboard — and never a drag (FEATURE-4F5C).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryTileMenuTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryTileMenuTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static async Task<(RepositoriesPageViewModel Model, RepositoriesPageView Page, Window Window, ListedRepository Entry)> ShowOneAsync(
        TestServices services)
    {
        string path = Path.Combine(services.ConfigurationRoot, "listed");
        Directory.CreateDirectory(path);
        await services.Get<IRepositoryListStore>().AddAsync(IdentityProfile.DefaultId, path, "listed");

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        RepositoriesPageView page = services.Get<RepositoriesPageView>();
        page.DataContext = model;

        Window window = new() { Content = page, Width = 1100, Height = 700 };
        window.Show();
        window.UpdateLayout();

        return (model, page, window, Assert.Single(model.Repositories));
    }

    /// <summary>
    /// The listed tile — not the drag copy's, which is hidden between drags.
    /// </summary>
    private static Border Tile(RepositoriesPageView page)
        => page.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Single(list => list.Name == "RepositoryList")
            .GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Classes.Contains("tile"));

    private static ContextMenu OpenMenu(Border tile, Window window)
    {
        ContextMenu menu = Assert.IsType<ContextMenu>(tile.ContextMenu);
        menu.Open(tile);
        window.UpdateLayout();

        return menu;
    }

    private static object[] Entries(ContextMenu menu)
        => [.. menu.Items.Cast<object>()];

    private static MenuItem Item(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => item.Header as string == header);

    [Fact]
    public void TheMenu_HoldsTheItemsInTheirOrder_EachWithItsGlyph()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (_, RepositoriesPageView page, Window window, _) = await ShowOneAsync(services);

            try
            {
                ContextMenu menu = OpenMenu(Tile(page), window);

                string[] read = [.. Entries(menu).Select(entry => entry is MenuItem item ? (string)item.Header! : "|")];

                Assert.Equal(
                    ["Open", "Open in a new window", "|",
                     "Open the repository's folder", "Open a terminal in the repository's folder", "Copy repository path", "|",
                     "Forget this repository", "|",
                     "Choose an icon…", "Use the default icon"],
                    read);

                // Going back to the folder is offered only once there is an icon to undo.
                Assert.False(Item(menu, "Use the default icon").IsVisible);

                (string Header, PhosphorIcon Glyph)[] glyphs =
                [
                    ("Open", PhosphorIcon.ArrowSquareIn),
                    ("Open in a new window", PhosphorIcon.ArrowSquareOut),
                    ("Open the repository's folder", PhosphorIcon.FolderOpen),
                    ("Open a terminal in the repository's folder", PhosphorIcon.TerminalWindow),
                    ("Copy repository path", PhosphorIcon.Copy),
                    ("Forget this repository", PhosphorIcon.X),
                    ("Choose an icon…", PhosphorIcon.ImageSquare),
                ];

                foreach ((string header, PhosphorIcon glyph) in glyphs)
                {
                    Icon icon = Assert.IsType<Icon>(Item(menu, header).Icon);

                    Assert.Equal(glyph, icon.Kind);
                    Assert.Contains("menu", icon.Classes);
                    Assert.True(icon.IsEffectivelyVisible, $"\"{header}\" draws no glyph");
                }

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EachItem_IsThePagesCommand_ForTheTilesRepository()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window, ListedRepository entry) = await ShowOneAsync(services);

            try
            {
                ContextMenu menu = OpenMenu(Tile(page), window);

                (string Header, object Command)[] expected =
                [
                    ("Open", model.OpenListedCommand),
                    ("Open in a new window", model.OpenInNewWindowCommand),
                    ("Open the repository's folder", model.OpenFolderCommand),
                    ("Open a terminal in the repository's folder", model.OpenTerminalCommand),
                    ("Copy repository path", model.CopyPathCommand),
                    ("Forget this repository", model.ForgetCommand),
                    ("Choose an icon…", model.ChooseIconCommand),
                    ("Use the default icon", model.ResetIconCommand),
                ];

                foreach ((string header, object command) in expected)
                {
                    MenuItem item = Item(menu, header);

                    Assert.Same(command, item.Command);
                    Assert.Equal(entry, item.CommandParameter);
                }

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CopyRepositoryPath_PutsTheFullPathOnTheClipboard_EvenForAMovedRepository()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, _, Window window, ListedRepository entry) = await ShowOneAsync(services);

            try
            {
                await model.CopyPathCommand.ExecuteAsync(entry);
                Assert.Equal([entry.Path], services.Interop.Copied);

                // Gone from disk: the list still knows where it was, and that can be copied.
                Directory.Delete(entry.Path);
                await model.CopyPathCommand.ExecuteAsync(entry);

                Assert.Equal([entry.Path, entry.Path], services.Interop.Copied);
                Assert.Empty(services.InfoBar.Shown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ARightClick_OpensTheMenu_AndNeverCarriesTheTile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (_, RepositoriesPageView page, Window window, _) = await ShowOneAsync(services);

            try
            {
                Border tile = Tile(page);

                // On the tile's body, left of its name: not one of its buttons.
                Point start = tile.TranslatePoint(new Point(40, tile.Bounds.Height / 2), window)
                    ?? throw new InvalidOperationException("The tile is not in the window.");

                window.MouseDown(start, MouseButton.Right);
                window.MouseMove(new Point(start.X, start.Y + 30), RawInputModifiers.RightMouseButton);

                Assert.False(page.IsDragging);

                window.MouseUp(new Point(start.X, start.Y + 30), MouseButton.Right);

                Assert.False(page.IsDragging);
                Assert.True(tile.ContextMenu!.IsOpen, "a right-click did not open the tile's menu");

                tile.ContextMenu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheKeyboardsMenuRequest_OnATilesButton_OpensTheTilesMenu()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (_, RepositoriesPageView page, Window window, _) = await ShowOneAsync(services);

            try
            {
                Border tile = Tile(page);
                Button open = tile.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Open this repository");

                // What Shift+F10 or the menu key raises on the focused control.
                open.RaiseEvent(new ContextRequestedEventArgs());
                window.UpdateLayout();

                Assert.True(tile.ContextMenu!.IsOpen, "the keyboard's request did not open the tile's menu");
                Assert.NotEmpty(tile.ContextMenu.GetLogicalDescendants().OfType<MenuItem>());

                tile.ContextMenu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }
}
