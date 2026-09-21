using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives tag management and "check out anything": the dialogs, the detached-HEAD warning, and the
/// three answers to "you have uncommitted changes". What the tags page itself lists, filters and
/// selects is <c>TagsPageTests</c>; this is about the questions the operations ask before they act.
/// </summary>
/// <remarks>
/// Each of these paths can throw work away, so what is asserted is mostly what the client asked
/// before it acted — and that the answer it was given is the one it obeyed.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class TagsAndCheckoutTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public TagsAndCheckoutTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "tags"), "main");

        await CommitAsync(repository, "README.md", "# one\n", "Add the readme");
        await CommitAsync(repository, "src/app.txt", "one\n", "Add the application file");

        await GitAsync(repository, "tag", "v0.1.0", "HEAD~1");
        await GitAsync(repository, "tag", "-a", "v1.0.0", "-m", "First release");
        await GitAsync(repository, "branch", "topic", "HEAD~1");

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

    private static async Task<BranchesPageViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static async Task<TagsPageViewModel> OpenTagsAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        TagsPageViewModel page = services.Get<TagsPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    // ---------------------------------------------------------------- creating tags

    [Fact]
    public void Page_CreatesALightweightTagWhenNoMessageIsWritten()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenTagsAsync(services, await BuildRepositoryAsync(services));

            FillTagDialog(services, "v2.0.0", message: string.Empty);
            services.Dialogs.Result = DialogResult.Primary;

            await page.CreateCommand.ExecuteAsync(null);

            TagRowViewModel created = page.Tags.Single(tag => tag.Name == "v2.0.0");

            Assert.Equal("lightweight", created.Kind);
        });
    }

    [Fact]
    public void CreateTagDialog_SaysWhichKindTheInputWillProduce()
    {
        CreateTagDialogViewModel model = new([new BranchStartPoint("HEAD", "HEAD", "now")], []);

        Assert.Contains("lightweight", model.KindDescription, StringComparison.Ordinal);

        model.Message = "Something worth saying";

        Assert.Contains("annotated", model.KindDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTagDialog_RefusesANameThatIsTakenOrInvalid()
    {
        CreateTagDialogViewModel model = new([new BranchStartPoint("HEAD", "HEAD", "now")], ["v1.0.0"]);

        Assert.False(model.IsValid);

        model.Name = "v1.0.0";

        Assert.False(model.IsValid);
        Assert.Contains("already exists", model.ValidationMessage, StringComparison.Ordinal);

        model.Name = "bad name";

        Assert.Contains("spaces", model.ValidationMessage, StringComparison.Ordinal);

        model.Name = "v2.0.0";

        Assert.True(model.IsValid);
    }

    // ---------------------------------------------------------------- deleting tags

    [Fact]
    public void Page_AsksBeforeDeletingATag()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenTagsAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.DeleteCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v1.0.0"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("v1.0.0", message, StringComparison.Ordinal);

            // A tag is a label, and saying so is what stops the question feeling dangerous.
            Assert.Contains("commit it points at is not affected", message, StringComparison.Ordinal);
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
            Assert.Contains(page.Tags, tag => tag.Name == "v1.0.0");
        });
    }

    // ---------------------------------------------------------------- detaching HEAD

    [Fact]
    public void Checkout_WarnsBeforeDetachingAndStopsWhenCancelled()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenTagsAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.CheckoutCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v0.1.0"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("detach", services.Dialogs.Last.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("belong to no branch", message, StringComparison.Ordinal);

            // Cancelled, so HEAD has not moved.
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void Checkout_DetachesOnceTheWarningIsAccepted()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenTagsAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Primary;

            await page.CheckoutCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v0.1.0"));

            Assert.True(services.Get<IRepositoryContext>().Head?.IsDetached);

            // And it says so afterwards, because a detached HEAD is easy to forget you are on.
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "HEAD is detached");
        });
    }

    [Fact]
    public void Checkout_OffersToCreateABranchInsteadOfDetaching()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            TagsPageViewModel page = await OpenTagsAsync(services, await BuildRepositoryAsync(services));

            // "Create a branch here…" on the warning, then the branch dialog itself.
            services.Dialogs.Script(DialogResult.Secondary, DialogResult.Primary);
            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateBranchDialogViewModel model })
                {
                    model.Name = "release-branch";
                    model.CheckoutAfterCreate = true;
                }
            };

            await page.CheckoutCommand.ExecuteAsync(page.Tags.Single(tag => tag.Name == "v0.1.0"));

            // A branch, not a detached HEAD — which is the entire point of offering it.
            Assert.Equal("release-branch", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.False(services.Get<IRepositoryContext>().Head?.IsDetached);
        });
    }

    [Fact]
    public void Checkout_DoesNotWarnWhenMovingToALocalBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            BranchRowViewModel topic = page.Groups.SelectMany(g => g.Rows).Single(row => row.FullName == "topic");

            await page.CheckoutCommand.ExecuteAsync(topic);

            Assert.Empty(services.Dialogs.Shown);
            Assert.Equal("topic", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    // ---------------------------------------------------------------- a dirty work tree

    [Theory]
    [InlineData("stash")]
    [InlineData("discard")]
    [InlineData("cancel")]
    public void Checkout_ObeysTheAnswerAboutUncommittedChanges(string answer)
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // "src/app.txt" does not exist on "topic", so an uncommitted edit to it is genuinely in
            // the way of the checkout.
            await File.WriteAllTextAsync(
                Path.Combine(repository.WorkTreePath, "src", "app.txt"),
                "edited but not committed\n");

            BranchesPageViewModel page = await OpenAsync(services, repository);
            BranchRowViewModel topic = page.Groups.SelectMany(g => g.Rows).Single(row => row.FullName == "topic");

            services.Dialogs.Result = answer switch
            {
                "stash" => DialogResult.Primary,
                "discard" => DialogResult.Secondary,
                _ => DialogResult.Close,
            };

            await page.CheckoutCommand.ExecuteAsync(topic);

            IRepositoryContext context = services.Get<IRepositoryContext>();

            if (answer == "cancel")
            {
                Assert.Equal("main", context.Head?.BranchName);
                Assert.Equal(
                    "edited but not committed\n",
                    await File.ReadAllTextAsync(Path.Combine(repository.WorkTreePath, "src", "app.txt")));

                return;
            }

            Assert.Equal("topic", context.Head?.BranchName);

            // Either way the file is gone from the work tree — the difference is whether it can be
            // brought back.
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "src", "app.txt")));

            string stash = await RunGitAsync(repository, "stash", "list");

            if (answer == "stash")
            {
                Assert.Contains("Before checking out topic", stash, StringComparison.Ordinal);
                Assert.Contains(services.InfoBar.Shown, note => note.Title == "Changes stashed");
            }
            else
            {
                Assert.Equal(string.Empty, stash.Trim());
            }
        });
    }

    [Fact]
    public void Checkout_NamesTheFilesADiscardWouldTake()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            await File.WriteAllTextAsync(
                Path.Combine(repository.WorkTreePath, "src", "app.txt"),
                "edited but not committed\n");

            BranchesPageViewModel page = await OpenAsync(services, repository);
            BranchRowViewModel topic = page.Groups.SelectMany(g => g.Rows).Single(row => row.FullName == "topic");

            services.Dialogs.Result = DialogResult.Close;

            await page.CheckoutCommand.ExecuteAsync(topic);

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("src/app.txt", message, StringComparison.Ordinal);
            Assert.Contains("1 changed file", message, StringComparison.Ordinal);
            Assert.Contains("Stashing keeps them", message, StringComparison.Ordinal);
        });
    }

    // ---------------------------------------------------------------- the graph's own menu

    [Fact]
    public void History_CreatesATagAtTheRowTheMenuWasOpenedOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Add the readme");

            string? chosenTarget = null;

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateTagDialogViewModel model })
                {
                    model.Name = "from-the-graph";
                    chosenTarget = model.SelectedTarget?.Revision;
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await row.Commands!.CreateTagHere.ExecuteAsync(row);

            Assert.Equal(row.Sha, chosenTarget);

            TagsPageViewModel page = await OpenTagsAsync(services, repository);

            Assert.Contains(page.Tags, tag => tag.Name == "from-the-graph");
        });
    }

    [Fact]
    public void History_ChecksOutACommitDetached()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Add the readme");

            services.Dialogs.Result = DialogResult.Primary;

            await row.Commands!.CheckoutCommit.ExecuteAsync(row);

            IRepositoryContext context = services.Get<IRepositoryContext>();

            Assert.True(context.Head?.IsDetached);
            Assert.Equal(row.Sha, context.Head?.Sha);
        });
    }

    [Fact]
    public void History_ChecksOutARowsBranchFromItsMenu()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Add the readme");

            Assert.True(row.HasBranch);
            Assert.Equal("topic", row.BranchName);

            await row.Commands!.CheckoutBranch.ExecuteAsync(row);

            // The menu is the only way HEAD moves now, and it moves onto the branch itself.
            Assert.Equal("topic", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void History_ChecksOutABranchlessRowFromItsMenuAndDetaches()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await CommitAsync(repository, "src/extra.txt", "extra\n", "Move main along");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(
                candidate => candidate.Subject == "Add the application file");

            Assert.False(row.HasBranch);

            services.Dialogs.Result = DialogResult.Primary;

            await row.Commands!.CheckoutCommit.ExecuteAsync(row);

            Assert.True(services.Get<IRepositoryContext>().Head?.IsDetached);
        });
    }

    [Fact]
    public void History_ActivatingARowChecksNothingOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            IRepositoryContext context = services.Get<IRepositoryContext>();
            string? before = context.Head?.BranchName;

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Add the readme");

            Assert.True(row.HasBranch);

            row.Commands!.Activate.Execute(row);

            // A double-click shows what the row changed; moving HEAD is the menu's job alone.
            Assert.Equal(before, context.Head?.BranchName);
            Assert.False(context.Head?.IsDetached);
            Assert.True(history.IsDiffViewOpen);
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_DrawsItsTags()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                TagsPageViewModel page = await OpenTagsAsync(services, await BuildRepositoryAsync(services));

                TagsPageView view = services.Get<TagsPageView>();
                view.DataContext = page;

                Window window = new() { Content = view, Width = 1100, Height = 420 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "tags-page.png");

                int colours = 0;

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    window.UpdateLayout();
                    window.InvalidateVisual();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The tags page produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);

                    if (colours >= 8)
                    {
                        break;
                    }
                }

                Assert.True(colours >= 8, "the tags page drew nothing");

                string[] texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("v1.0.0", texts);
                Assert.Contains("v0.1.0", texts);
                Assert.Contains("annotated", texts);
                Assert.Contains("lightweight", texts);
                Assert.Contains("First release", texts);

                window.Content = null;
                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static void FillTagDialog(TestServices services, string name, string message)
        => services.Dialogs.OnShown = dialog =>
        {
            if (dialog.Content is Control { DataContext: CreateTagDialogViewModel model })
            {
                model.Name = name;
                model.Message = message;
            }
        };

    private static async Task<string> RunGitAsync(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
    }
}
