using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the branches page against a real repository. The point of most of these is not that the
/// branch moved — the service's own tests prove that — but that the page asked first: every
/// destructive path here is gated by a dialog, and a cancelled dialog must leave the repository
/// exactly as it was.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BranchesPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public BranchesPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Builds a repository with a merged branch, an unmerged one and a remote, and opens it.
    /// </summary>
    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "branches"), "main");

        await CommitAsync(repository, "README.md", "# one\n", "Add the readme");
        await CommitAsync(repository, "src/app.txt", "one\n", "Add the application file");

        // A branch behind main: nothing would be lost by deleting it.
        await GitAsync(repository, "branch", "merged", "HEAD~1");

        // A branch with a commit of its own: deleting it loses work.
        await GitAsync(repository, "checkout", "-b", "unmerged");
        await CommitAsync(repository, "src/branch.txt", "only here\n", "Work only on the branch");
        await GitAsync(repository, "checkout", "main");

        return repository;
    }

    private static async Task<RepositoryHandle> BuildWithRemoteAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services);

        string originPath = Path.Combine(services.ConfigurationRoot, "workspace", "origin.git");
        Directory.CreateDirectory(originPath);

        await GitAsync(repository, "init", "--bare", originPath);
        await GitAsync(repository, "remote", "add", "origin", originPath);
        await GitAsync(repository, "push", "origin", "main");
        await GitAsync(repository, "push", "origin", "unmerged:published");
        await GitAsync(repository, "fetch", "origin");

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

    private static IReadOnlyList<string> NamesOf(BranchesPageViewModel page)
        => [.. page.Groups.SelectMany(group => group.Rows).Select(row => row.FullName)];

    // ---------------------------------------------------------------- what the page shows

    [Fact]
    public void Page_IsEmptyWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();

            Assert.True(page.IsEmpty);
            Assert.Contains("Open a repository", page.EmptyMessage, StringComparison.Ordinal);
            Assert.False(page.CreateBranchCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_ListsEveryLocalBranchAndMarksTheCheckedOutOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            BranchGroupViewModel local = Assert.Single(page.Groups);

            Assert.Equal("Local", local.Title);
            Assert.False(local.IsRemote);
            Assert.Equal(["main", "merged", "unmerged"], local.Rows.Select(row => row.FullName).Order());

            BranchRowViewModel main = local.Rows.Single(row => row.FullName == "main");

            Assert.True(main.IsCurrent);
            Assert.Equal("Add the application file", main.TipSubject);
            Assert.Equal("Ada Lovelace", main.TipAuthor);
            Assert.Equal(7, main.ShortSha.Length);
        });
    }

    [Fact]
    public void Page_GroupsRemoteBranchesUnderTheirRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            BranchGroupViewModel remote = page.Groups.Single(group => group.IsRemote);

            Assert.Equal("origin", remote.Title);
            Assert.Contains(remote.Rows, row => row.FullName == "origin/published");

            // The group already says which remote it is, so the row shows the bare name.
            Assert.Equal("published", remote.Rows.Single(row => row.FullName == "origin/published").Name);
        });
    }

    [Fact]
    public void Page_ShowsTheUpstreamAndHowFarApartTheyAre()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildWithRemoteAsync(services);

            await GitAsync(repository, "branch", "--set-upstream-to=origin/main", "main");
            await CommitAsync(repository, "src/ahead.txt", "ahead\n", "Move ahead of the remote");

            BranchesPageViewModel page = await OpenAsync(services, repository);
            BranchRowViewModel main = page.Groups.SelectMany(g => g.Rows).Single(row => row.FullName == "main");

            Assert.True(main.HasUpstream);
            Assert.Equal("origin/main", main.Upstream);
            Assert.True(main.IsAhead);
            Assert.Equal("1", main.Ahead);
            Assert.False(main.IsBehind);
        });
    }

    [Fact]
    public void Page_FiltersByName()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            page.SearchText = "merge";

            Assert.Equal(["merged", "unmerged"], NamesOf(page).Order());

            page.SearchText = "nothing like this";

            Assert.True(page.IsEmpty);
            Assert.Contains("No branch matches", page.EmptyMessage, StringComparison.Ordinal);

            page.ClearSearchCommand.Execute(null);

            Assert.Equal(3, NamesOf(page).Count);
        });
    }

    // ---------------------------------------------------------------- selection

    [Fact]
    public void Items_AreEachGroupsHeadingFollowedByItsBranches()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            // The flat list is the grouping, in the same order: a heading, then the branches under
            // it, then the next heading.
            List<string> shape = [.. page.Items.Select(item => item switch
            {
                BranchGroupHeaderViewModel header => $"# {header.Title}",
                BranchRowViewModel row => row.FullName,
                _ => "?",
            })];

            Assert.Equal("# Local", shape[0]);
            Assert.Contains("# origin", shape);
            Assert.True(shape.IndexOf("# origin") > shape.IndexOf("main"), "the remote group came before the local one");

            // Every branch in the groups is in the list, and nothing else is.
            Assert.Equal(
                page.Groups.SelectMany(group => group.Rows).Select(row => row.FullName).Order(),
                page.Items.OfType<BranchRowViewModel>().Select(row => row.FullName).Order());

            Assert.Equal(page.Groups.Count, page.Items.OfType<BranchGroupHeaderViewModel>().Count());
        });
    }

    [Fact]
    public void Heading_IsNotSelectable()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.All(page.Items.OfType<BranchGroupHeaderViewModel>(), header => Assert.False(header.IsSelectable));
            Assert.All(page.Items.OfType<BranchRowViewModel>(), row => Assert.True(row.IsSelectable));
        });
    }

    [Fact]
    public void Selection_IsTheBranchTheReaderPicked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.Null(page.SelectedItem);
            Assert.Null(page.SelectedBranch);

            page.SelectedItem = Row(page, "merged");

            Assert.NotNull(page.SelectedBranch);
            Assert.Equal("merged", page.SelectedBranch!.FullName);

            // A heading can be put there by nothing the view offers, and it is not a branch either
            // way.
            page.SelectedItem = page.Items.OfType<BranchGroupHeaderViewModel>().First();

            Assert.Null(page.SelectedBranch);
        });
    }

    [Fact]
    public void Selection_SurvivesARebuildAndGoesWithTheBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedItem = Row(page, "merged");

            await page.RefreshAsync();

            // A new row object for the same branch: the selection followed the name, not the
            // instance.
            Assert.NotNull(page.SelectedBranch);
            Assert.Equal("merged", page.SelectedBranch!.FullName);
            Assert.Same(Row(page, "merged"), page.SelectedItem);

            // A filter that hides it takes the selection with it, and putting it back does not
            // guess that the reader still wants it.
            page.SearchText = "unmerged";

            Assert.Null(page.SelectedBranch);

            page.SearchText = string.Empty;

            Assert.Null(page.SelectedBranch);
        });
    }

    [Fact]
    public void Selection_IsClearedWhenTheBranchIsDeleted()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedItem = Row(page, "merged");
            Assert.NotNull(page.SelectedBranch);

            services.Dialogs.Result = DialogResult.Primary;
            await page.DeleteCommand.ExecuteAsync(Row(page, "merged"));

            Assert.DoesNotContain(page.Items.OfType<BranchRowViewModel>(), row => row.FullName == "merged");
            Assert.Null(page.SelectedBranch);
        });
    }

    [Fact]
    public void Selection_OfATagIsItsOwn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            await GitAsync(repository, "tag", "v1.0.0");
            await GitAsync(repository, "tag", "v1.1.0");

            BranchesPageViewModel page = await OpenAsync(services, repository);
            page.ShowTags = true;

            page.SelectedTag = page.Tags.Single(tag => tag.Name == "v1.0.0");

            await page.RefreshAsync();

            Assert.NotNull(page.SelectedTag);
            Assert.Equal("v1.0.0", page.SelectedTag!.Name);

            // The two lists are alternatives, so selecting a tag says nothing about the branches.
            Assert.Null(page.SelectedBranch);
        });
    }

    [Fact]
    public void BranchList_IsAListBoxWhoseSelectionFollowsThePage()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            ListBox list = view.FindControl<ListBox>("BranchList")
                ?? throw new InvalidOperationException("The branches page has no branch list.");

            Assert.Equal(page.Items.Count, list.ItemCount);

            list.SelectedItem = Row(page, "merged");
            window.UpdateLayout();

            Assert.Same(list.SelectedItem, page.SelectedItem);
            Assert.Equal("merged", page.SelectedBranch!.FullName);

            // The heading's container is disabled, which is what stops it being selected, and it is
            // drawn at full opacity because nothing about it is unavailable.
            ListBoxItem heading = list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .First(container => container.DataContext is BranchGroupHeaderViewModel);

            Assert.False(heading.IsEnabled);
            Assert.Equal(1, heading.Opacity);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- dropping one branch on another

    [Fact]
    public void Drop_CarriesWhatTheTwoRowsSay()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            BranchDrop drop = new(Row(page, "origin/published"), Row(page, "main"));

            Assert.Equal("origin/published", drop.Request.Source);
            Assert.True(drop.Request.SourceIsRemote);
            Assert.Equal("main", drop.Request.Target);
            Assert.False(drop.Request.TargetIsRemote);
            Assert.True(drop.Request.TargetIsCurrent);

            // Both ends are named in every item, so a drag that went the wrong way round is
            // recoverable from the menu it opened.
            Assert.Contains("origin/published", drop.MergeHeader, StringComparison.Ordinal);
            Assert.Contains("main", drop.MergeHeader, StringComparison.Ordinal);
            Assert.Contains("fast-forward", drop.FastForwardHeader, StringComparison.Ordinal);
            Assert.Equal("Merge \"main\" into \"origin/published\"", drop.ReversedHeader);

            Assert.Equal("main", drop.Reversed().Request.Source);
            Assert.Equal("origin/published", drop.Reversed().Request.Target);
        });
    }

    [Fact]
    public void Drop_IsOfferedOnlyForAPairThatMeansSomething()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            Assert.True(BranchesPageViewModel.CanDrop(new BranchDrop(Row(page, "merged"), Row(page, "main"))));
            Assert.False(BranchesPageViewModel.CanDrop(null));

            // A branch onto itself means nothing.
            Assert.False(BranchesPageViewModel.CanDrop(new BranchDrop(Row(page, "main"), Row(page, "main"))));

            // Nothing local writes to a remote-tracking ref: that is a push, and a push is not a
            // thing to arrive at by dragging.
            Assert.False(BranchesPageViewModel.CanDrop(
                new BranchDrop(Row(page, "main"), Row(page, "origin/published"))));

            // And the reverse of that pair is a merge, which is exactly why the menu offers it.
            BranchDrop backwards = new(Row(page, "origin/published"), Row(page, "main"));

            Assert.True(page.MergeDropCommand.CanExecute(backwards));
            Assert.False(page.MergeReversedDropCommand.CanExecute(backwards));
        });
    }

    [Fact]
    public void Drop_MergesTheDraggedBranchIntoTheOneItLandedOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);

            await page.MergeDropCommand.ExecuteAsync(new BranchDrop(Row(page, "unmerged"), Row(page, "main")));

            // The branch's own commit is on main now, and the page re-read to show it.
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/branch.txt")));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);

            // The menu was the question: no dialog was raised on top of it.
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void Drop_ChecksTheTargetOutWhenItIsNotTheCurrentBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            // "merged" is a branch behind main, so merging main into it is a fast-forward — and it
            // has to be checked out first, because git merges into HEAD.
            await page.FastForwardDropCommand.ExecuteAsync(new BranchDrop(Row(page, "main"), Row(page, "merged")));

            Assert.Equal("merged", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/app.txt")));
        });
    }

    [Fact]
    public void Drop_MergesTheOtherWayRoundWhenThatIsWhatWasMeant()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            // Dragged main onto unmerged, then picked the item that merges the other way: unmerged
            // goes into main, and HEAD stays where it was.
            await page.MergeReversedDropCommand.ExecuteAsync(new BranchDrop(Row(page, "main"), Row(page, "unmerged")));

            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/branch.txt")));
        });
    }

    [Fact]
    public void Drop_OnARemoteBranchRunsNothingAndSaysWhy()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildWithRemoteAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            BranchDrop onRemote = new(Row(page, "unmerged"), Row(page, "origin/published"));

            // The command refuses, so the menu never offers it; asked anyway, nothing runs and the
            // reader is told why.
            Assert.False(page.MergeDropCommand.CanExecute(onRemote));

            await page.MergeDropCommand.ExecuteAsync(onRemote);

            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void BranchList_TakesDrops()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            ListBox list = view.FindControl<ListBox>("BranchList")
                ?? throw new InvalidOperationException("The branches page has no branch list.");

            Assert.True(DragDrop.GetAllowDrop(list));

            // A row container marked as the drop target wears a ring the selection cannot hide.
            ListBoxItem row = list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .First(container => container.DataContext is BranchRowViewModel);

            row.Classes.Set("droptarget", true);
            window.UpdateLayout();

            Assert.True(row.BorderThickness.Left > 0);

            row.Classes.Set("droptarget", false);
            window.UpdateLayout();

            window.Close();
        });
    }

    [Theory]
    [InlineData(150, 0)]        // the middle of the list: nothing to do
    [InlineData(100, 0)]
    [InlineData(200, 0)]
    [InlineData(4, -1)]         // near the top: towards the top
    [InlineData(-20, -1)]       // and past it, which a fast pointer reaches
    [InlineData(296, 1)]        // near the bottom: towards the bottom
    [InlineData(400, 1)]
    public void ADragNearAnEdgeScrollsTheList(double y, int direction)
    {
        double scroll = BranchDragGesture.ScrollFor(y, viewportHeight: 300);

        Assert.Equal(direction, Math.Sign(scroll));
        Assert.True(Math.Abs(scroll) <= BranchDragGesture.ScrollStep);
    }

    [Fact]
    public void ADragScrollsFasterTheDeeperIntoTheEdgeItIs()
    {
        // Deeper into the band is faster, and the very edge is the whole step.
        double edge = Math.Abs(BranchDragGesture.ScrollFor(0, 300));
        double inside = Math.Abs(BranchDragGesture.ScrollFor(BranchDragGesture.ScrollBand - 2, 300));

        Assert.True(edge > inside, $"the edge scrolls by {edge} and the band's inside by {inside}");
        Assert.Equal(BranchDragGesture.ScrollStep, edge, 3);

        // It never falls to nothing inside the band: a list that stops scrolling short of its end
        // is a list whose last row cannot be dropped on.
        Assert.True(inside > 0);

        // And a list with no viewport scrolls by nothing rather than by NaN.
        Assert.Equal(0, BranchDragGesture.ScrollFor(10, 0));
    }

    [Fact]
    public void TheScrollBandNeverSwallowsAShortList()
    {
        // A third of the viewport at most, so a list two rows tall still has a middle.
        Assert.Equal(0, BranchDragGesture.ScrollFor(30, 60));

        Assert.True(BranchDragGesture.ScrollFor(2, 60) < 0);
        Assert.True(BranchDragGesture.ScrollFor(58, 60) > 0);
    }

    [Theory]
    [InlineData(true, true, DragDropEffects.Move)]     // a branch, over the list: the gesture is under way
    [InlineData(true, false, DragDropEffects.None)]    // a branch, somewhere else on the page
    [InlineData(false, true, DragDropEffects.None)]    // something else, over the list
    [InlineData(false, false, DragDropEffects.None)]
    public void TheDragCursorAnswersWhetherTheGestureIsUnderWay(bool carriesBranch, bool overTheList, DragDropEffects expected)
        => Assert.Equal(expected, BranchDragGesture.EffectFor(carriesBranch, overTheList));

    [Fact]
    public void TheDragCursorSaysYesEvenWhereTheDropWouldNot()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            // The pairs the policy refuses — a branch on itself, anything onto a remote — are still
            // refused: the ring and the menu are the policy's, and only the cursor changed.
            Assert.False(BranchesPageViewModel.CanDrop(new BranchDrop(Row(page, "main"), Row(page, "main"))));
            Assert.False(BranchesPageViewModel.CanDrop(
                new BranchDrop(Row(page, "main"), Row(page, "origin/published"))));

            // And while either of those is under the pointer, the cursor still says the drag is
            // running rather than that it is impossible.
            Assert.Equal(DragDropEffects.Move, BranchDragGesture.EffectFor(carriesBranch: true, isOverTheList: true));
        });
    }

    [Theory]
    [InlineData(0, 0, false)]          // a press and a release in the same place is a click
    [InlineData(1, 1, false)]          // and so is a shaky hand
    [InlineData(4, 0, true)]           // a deliberate move sideways
    [InlineData(0, -4, true)]          // or upwards
    [InlineData(-10, 12, true)]        // or anywhere else
    public void ADragStartsOnlyOnceThePointerHasMoved(double x, double y, bool isDrag)
        => Assert.Equal(isDrag, BranchDragGesture.IsDrag(new Point(100, 100), new Point(100 + x, 100 + y)));

    [Fact]
    public void TheDragThresholdIsTheSameInEveryDirection()
    {
        Point origin = new(50, 50);

        // The same distance, four ways: a threshold that is a distance and not a box.
        Assert.True(BranchDragGesture.IsDrag(origin, origin.WithX(origin.X + BranchDragGesture.Threshold)));
        Assert.True(BranchDragGesture.IsDrag(origin, origin.WithX(origin.X - BranchDragGesture.Threshold)));
        Assert.True(BranchDragGesture.IsDrag(origin, origin.WithY(origin.Y + BranchDragGesture.Threshold)));
        Assert.True(BranchDragGesture.IsDrag(origin, origin.WithY(origin.Y - BranchDragGesture.Threshold)));

        // And a pointer that has not travelled it is still a click, diagonally too.
        Assert.False(BranchDragGesture.IsDrag(origin, new Point(origin.X + 2, origin.Y + 2)));
    }

    [Fact]
    public void ASelectedBranchRow_ReadsInFull()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            ListBox list = view.FindControl<ListBox>("BranchList")
                ?? throw new InvalidOperationException("The branches page has no branch list.");

            ListBoxItem[] rows = [.. list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .Where(container => container.DataContext is BranchRowViewModel)];

            Assert.True(rows.Length >= 2, "the branches page realised fewer than two rows");

            Application application = Application.Current!;
            Assert.True(application.TryFindResource("EnigmaForegroundBrush", application.ActualThemeVariant, out object? full));

            // The tip's subject, its author and its date are the quiet columns of a branch row.
            static TextBlock[] Quiet(ListBoxItem row) =>
                [.. row.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(text => text.Classes.Contains("dim") || text.Classes.Contains("faint"))];

            Assert.NotEmpty(Quiet(rows[0]));

            list.SelectedItem = rows[0].DataContext;
            window.UpdateLayout();

            Assert.All(Quiet(rows[0]), text => Assert.Same(full, text.Foreground));
            Assert.All(Quiet(rows[1]), text => Assert.NotSame(full, text.Foreground));

            // And the one that loses the selection goes back to being quiet.
            list.SelectedItem = rows[1].DataContext;
            window.UpdateLayout();

            Assert.All(Quiet(rows[0]), text => Assert.NotSame(full, text.Foreground));
            Assert.All(Quiet(rows[1]), text => Assert.Same(full, text.Foreground));

            window.Close();
        });
    }

    // ---------------------------------------------------------------- creating

    [Fact]
    public void Page_CreatesTheBranchTheDialogDescribes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            FillCreateDialog(services, "feature/login", checkout: false);
            services.Dialogs.Result = DialogResult.Primary;

            await page.CreateBranchCommand.ExecuteAsync(null);

            Assert.Contains("feature/login", NamesOf(page));

            // "Check out afterwards" was off, so HEAD has not moved.
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void Page_CanCheckTheNewBranchOutImmediately()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            FillCreateDialog(services, "feature/checkout", checkout: true);
            services.Dialogs.Result = DialogResult.Primary;

            await page.CreateBranchCommand.ExecuteAsync(null);

            Assert.Equal("feature/checkout", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void Page_CreatesNothingWhenTheDialogIsCancelled()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            FillCreateDialog(services, "never-created", checkout: false);
            services.Dialogs.Result = DialogResult.Close;

            await page.CreateBranchCommand.ExecuteAsync(null);

            Assert.DoesNotContain("never-created", NamesOf(page));
        });
    }

    // ---------------------------------------------------------------- checkout and rename

    [Fact]
    public void Page_ChecksABranchOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            await page.CheckoutCommand.ExecuteAsync(Row(page, "unmerged"));

            Assert.Equal("unmerged", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.True(Row(page, "unmerged").IsCurrent);
        });
    }

    [Fact]
    public void Page_WillNotCheckOutTheBranchItIsAlreadyOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.False(page.CheckoutCommand.CanExecute(Row(page, "main")));
        });
    }

    [Fact]
    public void Page_RenamesABranchFromTheDialog()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RenameBranchDialogViewModel model })
                {
                    model.Name = "renamed";
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await page.RenameCommand.ExecuteAsync(Row(page, "merged"));

            Assert.Contains("renamed", NamesOf(page));
            Assert.DoesNotContain("merged", NamesOf(page));
        });
    }

    [Fact]
    public void Page_OffersRenameOnlyForALocalBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            BranchRowViewModel remote = page.Groups.SelectMany(g => g.Rows).First(row => row.IsRemote);

            Assert.False(page.RenameCommand.CanExecute(remote));
            Assert.False(page.SetUpstreamCommand.CanExecute(remote));
        });
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public void Page_AsksBeforeDeletingAndDeletesNothingOnCancel()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.DeleteCommand.ExecuteAsync(Row(page, "merged"));

            Assert.Single(services.Dialogs.Shown);
            Assert.Contains("merged", NamesOf(page));
        });
    }

    [Fact]
    public void Page_DeletesAMergedBranchOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Primary;

            await page.DeleteCommand.ExecuteAsync(Row(page, "merged"));

            Assert.DoesNotContain("merged", NamesOf(page));
        });
    }

    [Fact]
    public void Page_NamesTheCommitsAtRiskBeforeDeletingAnUnmergedBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.DeleteCommand.ExecuteAsync(Row(page, "unmerged"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            // "Are you sure?" teaches nothing; the subject of the commit that would be lost does.
            Assert.Contains("Work only on the branch", message, StringComparison.Ordinal);
            Assert.Contains("1 commit", message, StringComparison.Ordinal);

            // And the default button is the harmless one, so a stray Enter cannot delete it.
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
        });
    }

    [Fact]
    public void Page_DeletesAnUnmergedBranchOnlyAfterTheWarning()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            services.Dialogs.Result = DialogResult.Primary;

            await page.DeleteCommand.ExecuteAsync(Row(page, "unmerged"));

            Assert.DoesNotContain("unmerged", NamesOf(page));
        });
    }

    [Fact]
    public void Page_NeverOffersToDeleteTheCheckedOutBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            Assert.False(page.DeleteCommand.CanExecute(Row(page, "main")));
        });
    }

    [Fact]
    public void Page_WarnsBeforeDeletingABranchOnARemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            services.Dialogs.Result = DialogResult.Close;

            await page.DeleteCommand.ExecuteAsync(
                page.Groups.SelectMany(g => g.Rows).Single(row => row.FullName == "origin/published"));

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("for everyone", message, StringComparison.Ordinal);
            Assert.Contains("origin/published", NamesOf(page));
        });
    }

    // ---------------------------------------------------------------- upstream

    [Fact]
    public void Page_SetsAndClearsAnUpstream()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: SetUpstreamDialogViewModel model })
                {
                    model.Selected = "origin/published";
                }
            };

            services.Dialogs.Result = DialogResult.Primary;
            await page.SetUpstreamCommand.ExecuteAsync(Row(page, "merged"));

            Assert.Equal("origin/published", Row(page, "merged").Upstream);

            services.Dialogs.OnShown = null;
            services.Dialogs.Result = DialogResult.Secondary;
            await page.SetUpstreamCommand.ExecuteAsync(Row(page, "merged"));

            Assert.False(Row(page, "merged").HasUpstream);
        });
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public void Page_ReportsARefusalInsteadOfThrowing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            // Renaming onto a name that is taken is refused by the service before git runs.
            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RenameBranchDialogViewModel model })
                {
                    model.Name = "unmerged";
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await page.RenameCommand.ExecuteAsync(Row(page, "merged"));

            // The dialog itself refuses the name, so nothing is attempted and both branches remain.
            Assert.Contains("merged", NamesOf(page));
            Assert.Contains("unmerged", NamesOf(page));
        });
    }

    // ---------------------------------------------------------------- the graph's own menu

    [Fact]
    public void History_CreatesABranchAtTheRowTheMenuWasOpenedOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Add the readme");

            string? chosenStartPoint = null;

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateBranchDialogViewModel model })
                {
                    model.Name = "from-the-graph";
                    model.CheckoutAfterCreate = false;
                    chosenStartPoint = model.SelectedStartPoint?.Revision;
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await row.Commands!.CreateBranchHere.ExecuteAsync(row);

            // The dialog opened on the row's own commit, not on HEAD.
            Assert.Equal(row.Sha, chosenStartPoint);

            BranchesPageViewModel branches = await OpenAsync(services, repository);

            Assert.Contains("from-the-graph", NamesOf(branches));
        });
    }

    [Fact]
    public void History_ChecksOutTheBranchOnARow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Work only on the branch");

            Assert.True(row.HasBranch);
            Assert.Equal("unmerged", row.BranchName);
            Assert.Contains("unmerged", row.CheckoutHeader, StringComparison.Ordinal);
            Assert.True(row.Commands!.CheckoutBranch.CanExecute(row));

            await row.Commands.CheckoutBranch.ExecuteAsync(row);

            Assert.Equal("unmerged", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void History_OffersNoBranchActionsOnARowWithoutOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // Every commit of the fixture carries a branch, so main is moved on to leave one that
            // does not.
            await CommitAsync(repository, "src/extra.txt", "extra\n", "Move main along");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(
                candidate => candidate.Subject == "Add the application file");

            Assert.False(row.HasBranch);
            Assert.False(row.Commands!.CheckoutBranch.CanExecute(row));
            Assert.False(row.Commands.DeleteBranch.CanExecute(row));

            // Creating one is always available: every commit can be branched from.
            Assert.True(row.Commands.CreateBranchHere.CanExecute(row));
        });
    }

    [Fact]
    public void History_WillNotCheckOutTheBranchAlreadyCheckedOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel head = history.Rows.Single(row => row.IsHead);

            Assert.Equal("main", head.BranchName);
            Assert.False(head.Commands!.CheckoutBranch.CanExecute(head));
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_DrawsItsBranches()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

                BranchesPageView view = services.Get<BranchesPageView>();
                view.DataContext = page;

                Window window = new() { Content = view, Width = 1100, Height = 560 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "branches-page.png");

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
                        ?? throw new InvalidOperationException("The branches page produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);

                    if (colours >= 8)
                    {
                        break;
                    }
                }

                Assert.True(colours >= 8, "the branches page drew nothing");

                string[] texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("main", texts);
                Assert.Contains("Local", texts);
                Assert.Contains("origin", texts);
                Assert.Contains("checked out", texts);

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

    private static BranchRowViewModel Row(BranchesPageViewModel page, string name)
        => page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == name);

    private static void FillCreateDialog(TestServices services, string name, bool checkout)
        => services.Dialogs.OnShown = dialog =>
        {
            if (dialog.Content is Control { DataContext: CreateBranchDialogViewModel model })
            {
                model.Name = name;
                model.CheckoutAfterCreate = checkout;
            }
        };
}
