using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Choosing a repository's icon from its tile's menu, and going back to the folder glyph
/// (FEATURE-4F5C).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryIconChoiceTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryIconChoiceTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string Picture(TestServices services, string name, Color colour)
    {
        using RenderTargetBitmap bitmap = new(new PixelSize(16, 16));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(colour), new Rect(0, 0, 16, 16));
        }

        string folder = Path.Combine(services.ConfigurationRoot, "pictures");
        Directory.CreateDirectory(folder);

        string file = Path.Combine(folder, name);
        bitmap.Save(file, PngBitmapEncoderOptions.Default);

        return file;
    }

    private static async Task<(RepositoriesPageViewModel Model, RepositoriesPageView Page, Window Window)> ShowOneAsync(TestServices services)
    {
        string path = Path.Combine(services.ConfigurationRoot, "workspace", "listed");
        Directory.CreateDirectory(path);
        await services.Get<IRepositoryListStore>().AddAsync(IdentityProfile.DefaultId, path, "listed");

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        RepositoriesPageView page = services.Get<RepositoriesPageView>();
        page.DataContext = model;

        Window window = new() { Content = page, Width = 1100, Height = 700 };
        window.Show();
        window.UpdateLayout();

        return (model, page, window);
    }

    private static T InTile<T>(RepositoriesPageView page, string name)
        where T : Control
        => page.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Single(list => list.Name == "RepositoryList")
            .GetVisualDescendants()
            .OfType<T>()
            .First(control => control.Name == name);

    private static ContextMenu OpenMenu(RepositoriesPageView page, Window window)
    {
        Border tile = page.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Single(list => list.Name == "RepositoryList")
            .GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Classes.Contains("tile"));

        ContextMenu menu = tile.ContextMenu!;
        menu.Open(tile);
        window.UpdateLayout();

        return menu;
    }

    private static MenuItem Item(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => item.Header as string == header);

    [Fact]
    public void ChoosingAPicture_MakesItTheTilesIcon()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowOneAsync(services);

            try
            {
                ListedRepository entry = Assert.Single(model.Repositories);
                services.Files.Answer = Picture(services, "logo.png", Colors.Red);

                await model.ChooseIconCommand.ExecuteAsync(entry);
                window.UpdateLayout();

                // The picker named the repository, and offered the kinds of file an icon can be.
                FilePickerOpenOptions request = Assert.Single(services.Files.Requests);
                Assert.Equal("Choose an icon for listed", request.Title);
                Assert.False(request.AllowMultiple);
                FilePickerFileType images = request.FileTypeFilter![0];
                Assert.Equal(
                    ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.ico", "*.webp", "*.svg"],
                    images.Patterns);

                string? stored = services.Get<IRepositoryIconStore>().Find(entry.Path);
                Assert.NotNull(stored);
                Assert.Equal(stored, Assert.Single(model.Repositories).IconFile);

                Assert.True(InTile<Image>(page, "TileImage").IsVisible);
                Assert.False(InTile<Icon>(page, "TileGlyph").IsVisible);
                Assert.Empty(services.InfoBar.Shown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ACancelledPicker_ChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowOneAsync(services);

            try
            {
                ListedRepository entry = Assert.Single(model.Repositories);
                services.Files.Answer = null;

                await model.ChooseIconCommand.ExecuteAsync(entry);
                window.UpdateLayout();

                Assert.Single(services.Files.Requests);
                Assert.Null(services.Get<IRepositoryIconStore>().Find(entry.Path));
                Assert.True(InTile<Icon>(page, "TileGlyph").IsVisible);
                Assert.Empty(services.InfoBar.Shown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AFileThatCannotBeAnIcon_IsReported_AndTheTileKeepsItsIcon()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowOneAsync(services);

            try
            {
                services.Files.Answer = Picture(services, "logo.png", Colors.Red);
                await model.ChooseIconCommand.ExecuteAsync(Assert.Single(model.Repositories));
                string? before = Assert.Single(model.Repositories).IconFile;

                string damaged = Path.Combine(services.ConfigurationRoot, "pictures", "damaged.png");
                File.WriteAllText(damaged, "no picture here");
                services.Files.Answer = damaged;

                await model.ChooseIconCommand.ExecuteAsync(Assert.Single(model.Repositories));
                window.UpdateLayout();

                RecordedNotification note = Assert.Single(services.InfoBar.Shown);
                Assert.Equal("The icon was not changed", note.Title);
                Assert.Equal(InfoBarSeverity.Error, note.Severity);
                Assert.Contains("damaged.png", note.Message, StringComparison.Ordinal);

                Assert.NotNull(before);
                Assert.Equal(before, Assert.Single(model.Repositories).IconFile);
                Assert.True(InTile<Image>(page, "TileImage").IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void UseTheDefaultIcon_IsOfferedOnlyWithAnIcon_AndPutsTheFolderBack()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowOneAsync(services);

            try
            {
                ContextMenu menu = OpenMenu(page, window);
                Assert.True(Item(menu, "Choose an icon…").IsVisible);
                Assert.False(Item(menu, "Use the default icon").IsVisible);
                menu.Close();

                services.Files.Answer = Picture(services, "logo.png", Colors.Red);
                await model.ChooseIconCommand.ExecuteAsync(Assert.Single(model.Repositories));
                window.UpdateLayout();

                menu = OpenMenu(page, window);
                MenuItem reset = Item(menu, "Use the default icon");
                Assert.True(reset.IsVisible);
                Assert.Same(model.ResetIconCommand, reset.Command);
                Assert.Equal(Assert.Single(model.Repositories), reset.CommandParameter);
                menu.Close();

                await model.ResetIconCommand.ExecuteAsync(Assert.Single(model.Repositories));
                window.UpdateLayout();

                Assert.Null(Assert.Single(model.Repositories).IconFile);
                Assert.Null(services.Get<IRepositoryIconStore>().Find(Assert.Single(model.Repositories).Path));
                Assert.True(InTile<Icon>(page, "TileGlyph").IsVisible);
                Assert.False(InTile<Image>(page, "TileImage").IsVisible);

                menu = OpenMenu(page, window);
                Assert.False(Item(menu, "Use the default icon").IsVisible);
                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheIcon_FollowsTheRepository_IntoEveryProfileThatListsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));

            string path = Path.Combine(services.ConfigurationRoot, "workspace", "shared");
            Directory.CreateDirectory(path);
            IRepositoryListStore lists = services.Get<IRepositoryListStore>();
            await lists.AddAsync(work.Id, path, "shared");
            await lists.AddAsync(home.Id, path, "shared");

            RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
            await model.OnAppearingAsync();

            services.Files.Answer = Picture(services, "logo.png", Colors.Red);
            await model.ChooseIconCommand.ExecuteAsync(Assert.Single(model.Repositories));
            string? icon = Assert.Single(model.Repositories).IconFile;
            Assert.NotNull(icon);

            model.SelectedProfile = model.Profiles.Single(profile => profile.Id != model.SelectedProfile!.Id);

            for (int attempt = 0; attempt < 300 && model.Repositories.Count != 1; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.Equal(icon, Assert.Single(model.Repositories).IconFile);

            // And adding the repository to a list again — opening it — keeps it.
            await lists.AddAsync(home.Id, path, "shared");
            await lists.AddAsync(work.Id, path, "shared");

            Assert.Equal(icon, services.Get<IRepositoryIconStore>().Find(path));
        });
    }
}
