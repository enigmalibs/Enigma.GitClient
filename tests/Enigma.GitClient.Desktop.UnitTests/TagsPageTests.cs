using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.GitClient.Desktop.Views.Panels;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Drives the tags page against a real repository. Tags used to be the other half of the branches
/// page, behind a switch; these are the tests that say the page they moved to lists, filters,
/// selects and acts on them in its own right.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TagsPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public TagsPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Builds a repository with a lightweight tag and an annotated one.
    /// </summary>
    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "tags-page"), "main");

        await CommitAsync(repository, "README.md", "# one\n", "Add the readme");
        await CommitAsync(repository, "src/app.txt", "one\n", "Add the application file");

        await GitAsync(repository, "tag", "v0.1.0", "HEAD~1");
        await GitAsync(repository, "tag", "-a", "v1.0.0", "-m", "First release");

        return repository;
    }

    private static async Task CommitAsync(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);

        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", message);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
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

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static async Task<TagsPageViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        // These tests are about the rows, not their order, and find them among the realised ones.
        services.OrderListsByName();

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        TagsPageViewModel page = services.Get<TagsPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    // ---------------------------------------------------------------- what the page shows

    [Fact]
    public void Page_IsEmptyWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            TagsPageViewModel page = services.Get<TagsPageViewModel>();

            Assert.True(page.IsEmpty);
            Assert.Contains("Open a repository", page.EmptyMessage, StringComparison.Ordinal);
            Assert.False(page.CreateCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_ListsEveryTagWithItsKind()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.Equal("Tags", page.Title);
            Assert.Equal(2, page.Tags.Count);

            TagRowViewModel lightweight = page.Tags.Single(tag => tag.Name == "v0.1.0");
            TagRowViewModel annotated = page.Tags.Single(tag => tag.Name == "v1.0.0");

            Assert.Equal("lightweight", lightweight.Kind);
            Assert.False(lightweight.HasMessage);
            Assert.False(lightweight.HasTagger);

            Assert.Equal("annotated", annotated.Kind);
            Assert.Equal("First release", annotated.Message);
            Assert.Equal("Ada Lovelace", annotated.Tagger);
            Assert.Equal(7, annotated.ShortSha.Length);
        });
    }

    [Fact]
    public void Page_FiltersItsTags()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.Equal("Filter tags", page.SearchPlaceholder);
            Assert.False(page.ClearSearchCommand.CanExecute(null));

            page.SearchText = "v1";

            Assert.Equal("v1.0.0", Assert.Single(page.Tags).Name);
            Assert.True(page.ClearSearchCommand.CanExecute(null));

            page.SearchText = "nothing";

            Assert.True(page.IsEmpty);
            Assert.Contains("No tag matches", page.EmptyMessage, StringComparison.Ordinal);

            page.ClearSearchCommand.Execute(null);

            Assert.Equal(2, page.Tags.Count);
        });
    }

    [Fact]
    public void Selection_SurvivesARefresh()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            page.SelectedTag = page.Tags.Single(tag => tag.Name == "v1.0.0");

            await page.RefreshAsync();

            // Every row is a new object after a rebuild, so the selection is restored by name.
            Assert.NotNull(page.SelectedTag);
            Assert.Equal("v1.0.0", page.SelectedTag!.Name);
        });
    }

    [Fact]
    public void Selection_GoesWithATagTheFilterHides()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            page.SelectedTag = page.Tags.Single(tag => tag.Name == "v0.1.0");
            page.SearchText = "v1";

            Assert.Null(page.SelectedTag);
        });
    }

    // ---------------------------------------------------------------- acting on a tag

    [Fact]
    public void Page_CreatesTheTagTheDialogDescribes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            FillTagDialog(services, "v2.0.0", message: "Second release");
            services.Dialogs.Result = DialogResult.Primary;

            await page.CreateCommand.ExecuteAsync(null);

            TagRowViewModel created = page.Tags.Single(tag => tag.Name == "v2.0.0");

            Assert.Equal("annotated", created.Kind);
            Assert.Equal("Second release", created.Message);
        });
    }

    [Fact]
    public void Page_CreatesNothingWhenTheDialogIsCancelled()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            FillTagDialog(services, "never-created", message: string.Empty);
            services.Dialogs.Result = DialogResult.Close;

            await page.CreateCommand.ExecuteAsync(null);

            Assert.DoesNotContain(page.Tags, tag => tag.Name == "never-created");
        });
    }

    [Fact]
    public void Page_AsksBeforeDeletingATagAndDeletesItOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.DeleteCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v1.0.0"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("v1.0.0", message, StringComparison.Ordinal);
            Assert.Contains(page.Tags, tag => tag.Name == "v1.0.0");

            services.Dialogs.Result = DialogResult.Primary;

            await page.DeleteCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v1.0.0"));

            Assert.DoesNotContain(page.Tags, tag => tag.Name == "v1.0.0");
        });
    }

    [Fact]
    public void Page_WarnsBeforeCheckingATagOutAndDetachesOnceAccepted()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.CheckoutCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v0.1.0"));

            Assert.Contains("detach", services.Dialogs.Last!.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);

            services.Dialogs.Result = DialogResult.Primary;

            await page.CheckoutCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v0.1.0"));

            Assert.True(services.Get<IRepositoryContext>().Head?.IsDetached);
        });
    }

    [Fact]
    public void Row_CarriesTheCommandsItsMenuBindsTo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            // The row carries the commands rather than a back-pointer to the page, which is what
            // lets a context menu in its own popup tree bind against the row itself.
            TagRowViewModel row = page.Tags.First();

            Assert.Same(page.CheckoutCommand, row.CheckoutCommand);
            Assert.Same(page.DeleteCommand, row.DeleteCommand);
        });
    }

    // ---------------------------------------------------------------- several at once

    [Fact]
    public void SeveralTags_GoAfterOneQuestionNamingThemAll_WithOneRefresh()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "tag", "v2.0.0");
            TagsPageViewModel page = await OpenAsync(services, repository);

            foreach (TagRowViewModel row in page.Tags.Where(tag => tag.Name != "v2.0.0"))
            {
                page.SelectedTags.Add(row);
            }

            Assert.Equal("Delete (2)", page.DeleteSelectionLabel);

            int refreshes = 0;
            services.Get<IRepositoryContext>().StateRefreshed += (_, _) => refreshes++;
            services.Dialogs.Result = DialogResult.Primary;

            await page.DeleteSelectionCommand.ExecuteAsync(null);

            ContentDialog question = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Delete 2 tags", question.Title);
            Assert.Equal(
                "Delete these 2 tags? The commits they point at are not affected.\n\n• v0.1.0\n• v1.0.0",
                question.Content);

            Assert.Equal(["v2.0.0"], page.Tags.Select(tag => tag.Name));
            Assert.Equal(1, refreshes);

            RecordedNotification summary = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Deleted 2 tags", summary.Title);
            Assert.Equal(InfoBarSeverity.Success, summary.Severity);
        });
    }

    [Fact]
    public void ATagThatCannotGo_LeavesTheOthersGoing_AndTheSummarySaysWhy()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            TagsPageViewModel page = await OpenAsync(services, repository);

            foreach (TagRowViewModel row in page.Tags)
            {
                page.SelectedTags.Add(row);
            }

            // Gone already, deleted in a terminal while the page still listed it.
            await GitAsync(repository, "tag", "-d", "v0.1.0");
            services.Dialogs.Result = DialogResult.Primary;

            await page.DeleteSelectionCommand.ExecuteAsync(null);

            Assert.Empty(page.Tags);

            RecordedNotification summary = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Deleted 1 of 2 tags", summary.Title);
            Assert.StartsWith("Not deleted:\nv0.1.0 — ", summary.Message, StringComparison.Ordinal);
            Assert.Contains("v0.1.0", summary.Message, StringComparison.Ordinal);
            Assert.Equal(InfoBarSeverity.Warning, summary.Severity);
        });
    }

    [Fact]
    public void TheDeleteButton_WaitsForASelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.Equal("Delete", page.DeleteSelectionLabel);
            Assert.False(page.DeleteSelectionCommand.CanExecute(null));

            page.SelectedTags.Add(page.Tags[0]);

            Assert.Equal("Delete (1)", page.DeleteSelectionLabel);
            Assert.True(page.DeleteSelectionCommand.CanExecute(null));
            Assert.False(page.Selection.IsMultiple);

            page.SelectedTags.Add(page.Tags[1]);
            Assert.True(page.Selection.IsMultiple);
        });
    }

    [Fact]
    public void ALinesMenu_ActsOnTheWholeSelection_WhenTheLineIsPartOfIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "tag", "v2.0.0");
            TagsPageViewModel page = await OpenAsync(services, repository);

            TagRowViewModel first = page.Tags.Single(tag => tag.Name == "v0.1.0");
            TagRowViewModel second = page.Tags.Single(tag => tag.Name == "v1.0.0");
            page.SelectedTags.Add(first);
            page.SelectedTags.Add(second);
            services.Dialogs.Result = DialogResult.Primary;

            await first.DeleteCommand.ExecuteAsync(first);

            Assert.Equal("Delete 2 tags", Assert.Single(services.Dialogs.Shown).Title);
            Assert.Equal(["v2.0.0"], page.Tags.Select(tag => tag.Name));
        });
    }

    // ---------------------------------------------------------------- the view

    [Fact]
    public void View_ShowsOneRowPerTag()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            ListBox list = view.FindControl<ListBox>("TagList")
                ?? throw new InvalidOperationException("The tags page has no tag list.");

            Assert.Equal(page.Tags.Count, list.ItemCount);

            // The page's selection is the list's, the way it is on every other page.
            list.SelectedItem = page.Tags.Single(tag => tag.Name == "v1.0.0");
            window.UpdateLayout();

            Assert.Same(list.SelectedItem, page.SelectedTag);

            ListBoxItem[] rows = [.. list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .Where(container => container.DataContext is TagRowViewModel)];

            Assert.Equal(page.Tags.Count, rows.Length);
            Assert.All(rows, row => Assert.True(row.Bounds.Height > 0));

            window.Close();
        });
    }

    [Fact]
    public void ATagLine_IsDrawnLikeAChangedFile_WithItsNameAloneAndTheRestInItsTooltip()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;

            // A changed file's line beside it, for the shape both have to share.
            ChangedFilesPanelViewModel files = new(new RecordingSystemInterop()) { ViewMode = ChangedFilesViewMode.List };
            files.SetFiles([new ChangedFile { Path = "README.md", AddedLines = 1, HasLineCounts = true }]);
            ChangedFilesPanelView filesView = new() { DataContext = files };

            Grid both = new() { ColumnDefinitions = new ColumnDefinitions("*,*") };
            both.Children.Add(view);
            both.Children.Add(filesView);
            Grid.SetColumn(filesView, 1);

            Window window = new() { Content = both, Width = 1400, Height = 420 };
            window.Show();
            window.UpdateLayout();

            ListBox list = view.FindControl<ListBox>("TagList")
                ?? throw new InvalidOperationException("The tags page has no tag list.");

            ListBoxItem[] rows = [.. list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .Where(container => container.DataContext is TagRowViewModel)];

            ListBoxItem fileRow = filesView.GetVisualDescendants().OfType<ListBoxItem>().First();

            Assert.True(rows.Length >= 2, "the tags page realised fewer than two rows");

            foreach (ListBoxItem row in rows)
            {
                TagRowViewModel tag = (TagRowViewModel)row.DataContext!;
                Grid line = row.GetVisualDescendants().OfType<Grid>().First(grid => grid.Classes.Contains("listrow"));

                Assert.Equal(fileRow.Bounds.Height, row.Bounds.Height);
                Assert.Equal(22, line.Bounds.Height);
                Assert.Empty(row.GetVisualDescendants().OfType<Button>());
                Assert.DoesNotContain(row.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("pill"));
                Assert.Equal(
                    [tag.Name],
                    row.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Text is { Length: > 0 }).Select(block => block.Text));
                Assert.Equal(tag.ToolTip, ToolTip.GetTip(line));
            }

            window.Close();
        });
    }

    [Fact]
    public void WithSeveralSelected_ALinesMenuOffersOnlyTheDeletes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            try
            {
                ListBox list = view.FindControl<ListBox>("TagList")!;
                Assert.Equal(SelectionMode.Multiple, list.SelectionMode);

                // As Ctrl+click would: both lines, through the list itself.
                list.SelectedItems!.Add(page.Tags[0]);
                list.SelectedItems.Add(page.Tags[1]);
                window.UpdateLayout();

                Assert.Equal(2, page.SelectedTags.Count);
                Assert.Equal("Delete (2)", view.FindControl<Button>("DeleteSelection")!.GetVisualDescendants().OfType<TextBlock>().Single().Text);

                Grid line = view.GetVisualDescendants().OfType<Grid>().First(grid => grid.ContextMenu is not null && grid.DataContext is TagRowViewModel);
                ContextMenu menu = line.ContextMenu!;
                menu.Open(line);

                string?[] offered = [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string)];
                Assert.Equal(["Delete locally…", "Delete from the remote…"], offered);

                menu.Close();

                // One line again: everything that works on one is back.
                list.SelectedItems.Remove(page.Tags[1]);
                menu.Open(line);

                Assert.Equal(5, menu.Items.OfType<MenuItem>().Count(item => item.IsVisible));
                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheDeleteKey_AsksAboutTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            try
            {
                ListBox list = view.FindControl<ListBox>("TagList")!;
                list.SelectedItems!.Add(page.Tags[0]);
                list.SelectedItems.Add(page.Tags[1]);

                // Where a click leaves the focus: on a line of the list.
                Assert.True(list.GetRealizedContainers().OfType<ListBoxItem>().First().Focus());

                // Cancelled: the question is the point, and nothing goes.
                services.Dialogs.Result = DialogResult.Close;
                window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

                for (int attempt = 0; attempt < 50 && services.Dialogs.Shown.Count == 0; attempt++)
                {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Equal("Delete 2 tags", Assert.Single(services.Dialogs.Shown).Title);
                Assert.Equal(2, page.Tags.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void View_ShowsItsEmptyStateWithNoTags()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            TagsPageViewModel page = services.Get<TagsPageViewModel>();

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            string[] texts = [.. view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)];

            Assert.Contains("Tags", texts);
            Assert.Contains("Open a repository to manage its tags.", texts);

            window.Close();
        });
    }

    private static void FillTagDialog(TestServices services, string name, string message)
        => services.Dialogs.OnShown = dialog =>
        {
            if (dialog.Content is Control { DataContext: CreateTagDialogViewModel model })
            {
                model.Name = name;
                model.Message = message;
            }
        };
}
