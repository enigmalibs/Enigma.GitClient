using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

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

    /// <summary>
    /// Builds the repository the tracking tests need: a branch that tracks its upstream and has
    /// drifted both ways, a branch that is on the remote without tracking it, branches that are on no
    /// remote at all, and one whose upstream has been deleted under it.
    /// </summary>
    private static async Task<RepositoryHandle> BuildTrackingWorldAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildWithRemoteAsync(services);

        string workspace = Path.Combine(services.ConfigurationRoot, "workspace");
        string originPath = Path.Combine(workspace, "origin.git");

        // "tracked" tracks origin/tracked, and both ends move: one commit here, one from a second
        // clone, so git reports it as one ahead and one behind.
        await GitAsync(repository, "checkout", "-b", "tracked", "main");
        await GitAsync(repository, "push", "--set-upstream", "origin", "tracked");

        string otherPath = Path.Combine(workspace, "other");
        await GitInAsync(workspace, "clone", originPath, otherPath);
        await GitInAsync(otherPath, "checkout", "tracked");

        await File.WriteAllTextAsync(Path.Combine(otherPath, "theirs.txt"), "from elsewhere\n");
        await GitInAsync(otherPath, "add", "--all");
        await GitInAsync(otherPath, "commit", "-m", "Work done elsewhere");
        await GitInAsync(otherPath, "push", "origin", "tracked");

        await CommitAsync(repository, "src/ours.txt", "from here\n", "Work done here");

        // "doomed" names an upstream that is then deleted on the remote, which git reports as [gone].
        await GitAsync(repository, "checkout", "-b", "doomed", "main");
        await GitAsync(repository, "push", "--set-upstream", "origin", "doomed");
        await GitAsync(repository, "push", "origin", "--delete", "doomed");

        await GitAsync(repository, "checkout", "main");
        await GitAsync(repository, "fetch", "--prune", "origin");

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
        => GitInAsync(repository.WorkTreePath, arguments);

    private static Task GitInAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
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
        // These tests are about the rows, not their order, and find them among the realised ones.
        services.OrderListsByName();

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

    // ---------------------------------------------------------------- where a branch stands

    [Fact]
    public void ALocalRow_NamesTheRemoteBranchItIsOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            // A branch that tracks its upstream says so through the upstream it names.
            BranchRowViewModel tracked = Row(page, "tracked");

            Assert.True(tracked.IsPublished);
            Assert.Equal("origin/tracked", tracked.PublishedAs);
            Assert.False(tracked.IsLocalOnly);

            // And one pushed with a plain `git push origin main` tracks nothing at all — git reports
            // no upstream for it — and is on the remote all the same. Reading the upstream alone would
            // call this branch local-only every time.
            BranchRowViewModel main = Row(page, "main");

            Assert.False(main.HasUpstream);
            Assert.True(main.IsPublished);
            Assert.Equal("origin/main", main.PublishedAs);
            Assert.Contains("origin/main", main.RemoteStateTip, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ALocalRow_SaysWhenTheBranchIsOnNoRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            // "unmerged" was pushed to origin under another name, so there is no origin/unmerged:
            // this branch only exists here, which is the state worth saying out loud.
            foreach (string name in (string[])["merged", "unmerged"])
            {
                BranchRowViewModel row = Row(page, name);

                Assert.True(row.IsLocalOnly, name);
                Assert.False(row.IsPublished, name);
                Assert.Null(row.PublishedAs);
                Assert.Contains("no remote", row.RemoteStateTip, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Fact]
    public void ALocalRow_CountsTheCommitsEachWay()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            // One commit made here and one made elsewhere and fetched: the branch has drifted both
            // ways, and the row can say so without running anything of its own.
            BranchRowViewModel tracked = Row(page, "tracked");

            Assert.True(tracked.IsAhead);
            Assert.Equal("1", tracked.Ahead);
            Assert.True(tracked.IsBehind);
            Assert.Equal("1", tracked.Behind);

            // The tooltips are for someone who does not already know what an arrow means — and they
            // count in words, so a single commit is not "1 commits".
            Assert.Equal("1 commit to push", tracked.AheadTip);
            Assert.Equal("1 commit to pull", tracked.BehindTip);

            // A branch level with its remote shows no counter at all.
            BranchRowViewModel main = Row(page, "main");

            Assert.False(main.IsAhead);
            Assert.False(main.IsBehind);
        });
    }

    [Fact]
    public void ALocalRow_WhoseUpstreamHasGone_IsLocalOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            // The upstream was deleted on the remote: the branch is on no remote any more, which is
            // what the line says — there is no "upstream gone" marker of its own.
            BranchRowViewModel doomed = Row(page, "doomed");

            Assert.True(doomed.IsUpstreamGone);
            Assert.False(doomed.IsPublished);
            Assert.True(doomed.IsLocalOnly);
            Assert.Contains("origin/doomed", doomed.RemoteStateTip, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ARemoteRow_TracksNothingOfItsOwn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            // A remote-tracking branch *is* the remote: ahead of what? Those rows used to draw two
            // counters that were always empty.
            BranchRowViewModel remote = Row(page, "origin/published");

            Assert.False(remote.IsLocal);
            Assert.False(remote.IsAhead);
            Assert.False(remote.IsBehind);
            Assert.False(remote.IsPublished);
            Assert.False(remote.IsLocalOnly);
            Assert.False(remote.IsUpstreamGone);
        });
    }

    [Fact]
    public void Filtering_DoesNotTakeABranchOffItsRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            // The lookup is built from the whole ref collection, not from the rows that survived the
            // filter: typing in the box hides branches, it does not unpublish them.
            page.SearchText = "track";

            Assert.Equal(["origin/tracked", "tracked"], NamesOf(page).Order());
            Assert.True(Row(page, "tracked").IsPublished);
            Assert.Equal("origin/tracked", Row(page, "tracked").PublishedAs);
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
    public void TheTree_IsLocalFirst_ThenEachRemote_WithEveryBranchUnderItsNode()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            Assert.Equal("Local", page.Groups[0].Title);
            Assert.False(page.Groups[0].IsRemote);
            Assert.Equal(["origin"], page.Groups.Skip(1).Select(group => group.Title));
            Assert.True(page.Groups[1].IsRemote);

            // Every branch is under its node, at whatever depth, and the top-level nodes start open.
            Assert.All(page.Groups[0].Rows, row => Assert.False(row.IsRemote));
            Assert.All(page.Groups[1].Rows, row => Assert.True(row.IsRemote));
            Assert.All(page.Groups, group => Assert.True(group.IsExpanded));
        });
    }

    [Fact]
    public void WhatTheReaderOpenedOrClosed_SurvivesARefresh_ButNotWhatTheyDidWhileFiltering()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "branch", "feature/tree", "main");
            await GitAsync(repository, "branch", "feature/watcher", "main");
            BranchesPageViewModel page = await OpenAsync(services, repository);

            BranchTreeNode Feature() => Assert.Single(page.Groups[0].Children, node => node.IsFolder);

            Assert.True(page.Groups[0].IsExpanded);
            Assert.False(Feature().IsExpanded);

            Feature().IsExpanded = true;
            page.Groups[0].IsExpanded = false;
            BranchTreeNode before = Feature();

            await page.RefreshAsync();

            // New nodes, open and closed as the reader left them.
            Assert.NotSame(before, Feature());
            Assert.True(Feature().IsExpanded);
            Assert.False(page.Groups[0].IsExpanded);

            // The filter opens everything above a match; closing a node meanwhile is not remembered.
            page.SearchText = "watch";

            Assert.True(page.Groups[0].IsExpanded);
            Assert.True(Feature().IsExpanded);

            Feature().IsExpanded = false;
            page.SearchText = string.Empty;

            Assert.True(Feature().IsExpanded);
            Assert.False(page.Groups[0].IsExpanded);
        });
    }

    [Fact]
    public void TheTree_KeepsAFolderTheReaderOpenedOpen_AcrossARefresh()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "branch", "feature/tree", "main");
            BranchesPageViewModel page = await OpenAsync(services, repository);

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 420 };
            window.Show();
            window.UpdateLayout();

            TreeView tree = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            TreeViewItem Folder() => tree.GetRealizedTreeContainers()
                .OfType<TreeViewItem>()
                .Single(container => container.DataContext is BranchFolderViewModel and not BranchGroupViewModel);

            Assert.False(Folder().IsExpanded);

            // What the expander does: the line's own state, which the node follows.
            Folder().SetCurrentValue(TreeViewItem.IsExpandedProperty, true);

            Assert.True(((BranchTreeNode)Folder().DataContext!).IsExpanded);

            await page.RefreshAsync();
            window.UpdateLayout();

            // The tree was rebuilt — new nodes, new lines — and the folder is still open.
            Assert.True(Folder().IsExpanded);
            Assert.True(((BranchTreeNode)Folder().DataContext!).IsExpanded);

            window.Close();
        });
    }

    [Fact]
    public void ATopLevelNodeOrAFolder_IsNeverTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildRepositoryAsync(services));

            page.SelectedItem = Row(page, "merged");

            // The tree can put a folder there — the arrows reach one — and the page keeps its branch.
            page.SelectedItem = page.Groups[0];

            Assert.Same(Row(page, "merged"), page.SelectedItem);
            Assert.Equal("merged", page.SelectedBranch?.FullName);
            Assert.All(page.Groups, group => Assert.True(group.IsFolder));
            Assert.All(page.Groups.SelectMany(group => group.Rows), row => Assert.False(row.IsFolder));
        });
    }

    [Fact]
    public void AClickOnAFolder_FoldsIt_AndTheBranchSelectedBeforeStaysSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "branch", "feature/tree", "main");
            BranchesPageViewModel page = await OpenAsync(services, repository);

            (Window window, TreeView tree) = ShowTree(services, page);

            try
            {
                Click(window, OnLine(Row(tree, "merged"), window));
                Assert.Equal("merged", page.SelectedBranch?.FullName);

                BranchTreeNode feature = Assert.Single(page.Groups[0].Children, node => node.IsFolder);
                Assert.False(feature.IsExpanded);

                Click(window, OnLine(Line(tree, feature), window));

                Assert.True(feature.IsExpanded);
                Assert.Same(Row(page, "merged"), page.SelectedItem);
                Assert.Same(Row(page, "merged"), tree.SelectedItem);

                // Further along the line, so it is a second click and not a double-click.
                Click(window, OnLine(Line(tree, feature), window, 240));

                Assert.False(feature.IsExpanded);

                // A double-click folds it once: the tree's own fold on the second press is undone.
                Point point = OnLine(Line(tree, feature), window);
                Click(window, point);
                Click(window, point);

                Assert.True(feature.IsExpanded);

                // A top-level node's line folds the same way.
                Click(window, OnLine(Line(tree, page.Groups[0]), window));

                Assert.False(page.Groups[0].IsExpanded);
                Assert.Same(Row(page, "merged"), page.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheKeyboard_DoesNotSelectAFolder()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "branch", "feature/tree", "main");
            BranchesPageViewModel page = await OpenAsync(services, repository);

            (Window window, TreeView tree) = ShowTree(services, page);

            try
            {
                Click(window, OnLine(Row(tree, "main"), window));
                Assert.Equal("main", page.SelectedBranch?.FullName);

                // The arrows move from branch to branch...
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Assert.Equal("merged", page.SelectedBranch?.FullName);

                Press(window, Key.Up, PhysicalKey.ArrowUp);
                Assert.Equal("main", page.SelectedBranch?.FullName);

                // ...and the line above "main" is the folder's, which folders come first to put there.
                Press(window, Key.Up, PhysicalKey.ArrowUp);

                Assert.Same(Row(page, "main"), page.SelectedItem);
                Assert.Same(Row(page, "main"), tree.SelectedItem);
            }
            finally
            {
                window.Close();
            }
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

            // A top-level node is not a branch, and the page keeps the branch it had.
            page.SelectedItem = page.Groups.First();

            Assert.Equal("merged", page.SelectedBranch?.FullName);
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

            Assert.DoesNotContain(page.Groups.SelectMany(group => group.Rows), row => row.FullName == "merged");
            Assert.Null(page.SelectedBranch);
        });
    }

    [Fact]
    public void BranchTree_IsATreeWhoseSelectionFollowsThePage()
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

            TreeView tree = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            Assert.Equal(page.Groups.Count, tree.ItemCount);

            tree.SelectedItem = Row(page, "merged");
            window.UpdateLayout();

            Assert.Same(tree.SelectedItem, page.SelectedItem);
            Assert.Equal("merged", page.SelectedBranch!.FullName);

            // The top-level node's container is open, as the node is.
            TreeViewItem local = tree.GetRealizedTreeContainers()
                .OfType<TreeViewItem>()
                .First(container => container.DataContext is BranchGroupViewModel);

            Assert.True(local.IsExpanded);

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
    public void Drop_MergeRecordsAMergeCommitEvenWhenAFastForwardWouldDo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            // main is simply behind unmerged, so git on its own would fast-forward — the item beside
            // this one does exactly that, which is why this one must not.
            await page.MergeDropCommand.ExecuteAsync(new BranchDrop(Row(page, "unmerged"), Row(page, "main")));

            Assert.Equal(2, GitProbe.ParentCount(repository));
        });
    }

    [Fact]
    public void Drop_MergingTheOtherWayRoundRecordsAMergeCommitToo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            await page.MergeReversedDropCommand.ExecuteAsync(new BranchDrop(Row(page, "main"), Row(page, "unmerged")));

            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.Equal(2, GitProbe.ParentCount(repository));
        });
    }

    [Fact]
    public void Drop_FastForwardOnlyMovesTheBranchWithoutAMergeCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            await page.FastForwardDropCommand.ExecuteAsync(new BranchDrop(Row(page, "unmerged"), Row(page, "main")));

            Assert.Equal(GitProbe.Sha(repository, "unmerged"), GitProbe.Sha(repository, "main"));
            Assert.Equal(1, GitProbe.ParentCount(repository));
        });
    }

    [Fact]
    public void ManualMerge_RecordsAMergeCommitEvenWhenAFastForwardWouldDo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedMergeSource = "unmerged";
            page.SelectedMergeDestination = "main";
            await page.ManualMergeCommand.ExecuteAsync(null);

            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/branch.txt")));
            Assert.Equal(2, GitProbe.ParentCount(repository));
        });
    }

    [Fact]
    public void ManualFastForward_MovesTheDestinationWithoutAMergeCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            BranchesPageViewModel page = await OpenAsync(services, repository);

            page.SelectedMergeSource = "unmerged";
            page.SelectedMergeDestination = "main";
            await page.ManualFastForwardCommand.ExecuteAsync(null);

            Assert.Equal(GitProbe.Sha(repository, "unmerged"), GitProbe.Sha(repository, "main"));
            Assert.Equal(1, GitProbe.ParentCount(repository));
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
    public void BranchList_MarksTheRowADropWouldLandOnAndTakesNoPlatformDrop()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            // The page runs the gesture itself and opens no platform drag session, so it is not a
            // platform drop target either: that machinery is what drew the refusal pointer.
            Assert.False(DragDrop.GetAllowDrop(list));

            // A row container marked as the drop target wears a ring the selection cannot hide.
            TreeViewItem row = list.GetRealizedTreeContainers()
                .OfType<TreeViewItem>()
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
    [InlineData(100, 100, true)]       // well inside the list
    [InlineData(0, 0, true)]           // the very corner counts as inside
    [InlineData(400, 300, true)]       // and so does the far edge
    [InlineData(-1, 100, false)]       // off to the left
    [InlineData(100, -1, false)]       // above the list, over the toolbar
    [InlineData(401, 100, false)]      // past its right edge
    [InlineData(100, 301, false)]      // below it
    public void ADragKnowsWhetherItIsStillOverTheList(double x, double y, bool over)
        => Assert.Equal(over, BranchDragGesture.IsOverTheList(new Point(x, y), new Size(400, 300)));

    [Fact]
    public void ADrag_OffersWhatTheTwoBranchesCanDo()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            // The whole gesture, through the real input system: press, move, release. The platform
            // session this replaced could not be driven by a test at all.
            Drag(window, Row(list, "unmerged"), Row(list, "main"));

            ContextMenu menu = Assert.IsType<ContextMenu>(view.DropMenu);

            string[] headers = [.. menu.ItemsSource!
                .OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? string.Empty)];

            Assert.Equal(3, headers.Length);
            Assert.All(menu.ItemsSource!.OfType<MenuItem>(), item => Assert.IsType<Icon>(item.Icon));
            Assert.All(headers, header =>
            {
                Assert.Contains("unmerged", header, StringComparison.Ordinal);
                Assert.Contains("main", header, StringComparison.Ordinal);
            });

            // And the items are the page's commands, with the pair as their parameter.
            MenuItem merge = menu.ItemsSource!.OfType<MenuItem>().First();

            Assert.Same(page.MergeDropCommand, merge.Command);

            BranchDrop drop = Assert.IsType<BranchDrop>(merge.CommandParameter);

            Assert.Equal("unmerged", drop.Source.FullName);
            Assert.Equal("main", drop.Target.FullName);

            menu.Close();
            window.Close();
        });
    }

    [Fact]
    public void ADrag_MarksTheRowUnderThePointerAndLetsItGoAtTheEnd()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            TreeViewItem source = Row(list, "unmerged");
            TreeViewItem target = Row(list, "main");
            TreeViewItem other = Row(list, "merged");

            Point start = Centre(source, window);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
            Assert.True(view.IsDragging);

            window.MouseMove(Centre(target, window), RawInputModifiers.LeftMouseButton);
            Assert.Contains("droptarget", target.Classes);

            // The mark follows the pointer: one row wears it at a time.
            window.MouseMove(Centre(other, window), RawInputModifiers.LeftMouseButton);
            Assert.Contains("droptarget", other.Classes);
            Assert.DoesNotContain("droptarget", target.Classes);

            window.MouseUp(Centre(other, window), MouseButton.Left);

            Assert.False(view.IsDragging);
            Assert.DoesNotContain("droptarget", other.Classes);

            view.DropMenu?.Close();
            window.Close();
        });
    }

    [Theory]
    [InlineData(true, StandardCursorType.DragMove)]   // over the list: the gesture is under way here
    [InlineData(false, null)]                          // anywhere else: the list keeps its own
    public void TheDragPointerSaysTheGestureIsUnderWay(bool overTheList, StandardCursorType? expected)
    {
        // Never StandardCursorType.No: the refusal pointer is the bug. Where a drop would land is the
        // ring's job, not the pointer's.
        Assert.Equal(expected, BranchDragGesture.CursorFor(overTheList));
    }

    [Fact]
    public void ADrag_WearsItsPointerAndGivesTheListItsOwnBack()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            Cursor? before = list.Cursor;

            TreeViewItem source = Row(list, "unmerged");
            Point start = Centre(source, window);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);

            // Set by the page, on the list, the ordinary way — which is exactly why it works where a
            // platform drag session's own cursor did not.
            Assert.NotSame(before, list.Cursor);
            Assert.NotNull(list.Cursor);

            window.MouseMove(Centre(Row(list, "main"), window), RawInputModifiers.LeftMouseButton);
            window.MouseUp(Centre(Row(list, "main"), window), MouseButton.Left);

            Assert.Same(before, list.Cursor);

            view.DropMenu?.Close();
            window.Close();
        });
    }

    [Fact]
    public void Escape_CallsADragOffAndDropsNothing()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            Cursor? before = list.Cursor;

            TreeViewItem target = Row(list, "main");
            Point start = Centre(Row(list, "unmerged"), window);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
            window.MouseMove(Centre(target, window), RawInputModifiers.LeftMouseButton);

            Assert.True(view.IsDragging);
            Assert.Contains("droptarget", target.Classes);

            // A platform drag session had a way out of its own; this one brings its own.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.False(view.IsDragging);
            Assert.DoesNotContain("droptarget", target.Classes);
            Assert.Same(before, list.Cursor);
            Assert.Null(view.DropMenu);

            // And letting go afterwards offers nothing: the gesture is over.
            window.MouseUp(Centre(target, window), MouseButton.Left);

            Assert.Null(view.DropMenu);

            // A drag that was called off can be started again.
            Drag(window, Row(list, "merged"), target);

            Assert.NotNull(view.DropMenu);

            view.DropMenu?.Close();
            window.Close();
        });
    }

    [Fact]
    public void AClick_SelectsTheRowAndStartsNothing()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            TreeViewItem row = Row(list, "merged");
            Point point = Centre(row, window);

            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(new Point(point.X + 1, point.Y), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);

            // A pointer that has not travelled the threshold is a click: the row is selected and
            // nothing is offered.
            Assert.False(view.IsDragging);
            Assert.Null(view.DropMenu);
            Assert.Equal("merged", page.SelectedBranch?.FullName);

            window.Close();
        });
    }

    [Fact]
    public void ADragReleasedOnNoRow_OffersNothing()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            Point start = Centre(Row(list, "unmerged"), window);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);

            // Up on the page's header, which is not the list: a drag let go of nowhere drops nothing.
            window.MouseMove(new Point(start.X, 20), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(start.X, 20), MouseButton.Left);

            Assert.False(view.IsDragging);
            Assert.Null(view.DropMenu);

            window.Close();
        });
    }

    [Fact]
    public void ADragOntoARowThePolicyRefuses_MarksNothingAndOffersNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildWithRemoteAsync(services));

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 560 };
            window.Show();
            window.UpdateLayout();

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            // Nothing local writes to a remote-tracking ref: that is a push, and a push is not a
            // thing to arrive at by dragging. The gesture still runs — the ring is what says no.
            TreeViewItem target = Row(list, "origin/published");

            Drag(window, Row(list, "main"), target);

            Assert.DoesNotContain("droptarget", target.Classes);
            Assert.Null(view.DropMenu);

            window.Close();
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            TreeViewItem[] rows = [.. list.GetRealizedTreeContainers()
                .OfType<TreeViewItem>()
                .Where(container => container.DataContext is BranchRowViewModel)];

            Assert.True(rows.Length >= 2, "the branches page realised fewer than two rows");

            Application application = Application.Current!;
            Assert.True(application.TryFindResource("EnigmaForegroundBrush", application.ActualThemeVariant, out object? full));

            // The tip's subject, its author and its date are the quiet columns of a branch row.
            static TextBlock[] Quiet(TreeViewItem row) =>
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

    [Fact]
    public void ABranchLine_IsDrawnLikeAChangedFile_WithItsLastNameSegmentAndItsBadges()
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

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            TreeViewItem main = Row(list, "main");
            Grid line = main.GetVisualDescendants().OfType<Grid>().First(grid => grid.Classes.Contains("listrow"));

            Assert.Equal(22, line.Bounds.Height);
            Assert.Equal(Row(page, "main").ToolTip, ToolTip.GetTip(line));

            // No button and no pill on the line: the chevron of the tree item is all there is.
            Assert.Empty(line.GetVisualDescendants().OfType<Button>());
            Assert.DoesNotContain(line.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("pill"));

            // Not bold: nothing on the line is drawn differently because it is the checked-out branch.
            Assert.All(line.GetVisualDescendants().OfType<TextBlock>(), text => Assert.NotEqual(FontWeight.SemiBold, text.FontWeight));

            string[] texts = [.. line.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && text.Text is { Length: > 0 })
                .Select(text => text.Text!)];

            // Its badges in their order: checked out, and — this repository has no remote — local only.
            Assert.Equal(["main", "checked out", "local only"], texts);

            window.Close();
        });
    }

    [Fact]
    public void ABranchLine_DrawsWhereItStandsWithItsRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 560 };
            window.Show();
            window.UpdateLayout();

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            // Only what the line actually draws: a hidden badge is still in the tree.
            string[] Badges(string branch) =>
                [.. Row(list, branch)
                    .GetVisualDescendants()
                    .OfType<StackPanel>()
                    .Where(badge => badge.Classes.Contains("badge") && badge.IsEffectivelyVisible)
                    .Select(badge => string.Join(' ', badge.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)))];

            // Drifted both ways: one count each, after its arrow.
            Assert.Equal(["1", "1"], Badges("tracked"));

            // Level with its remote: nothing to say at all — no "published" marker.
            Assert.Equal(["checked out"], Badges("main"));

            // On no remote, and pointing at an upstream that is gone: both only exist here now, and
            // there is no "upstream gone" marker.
            Assert.Equal(["local only"], Badges("merged"));
            Assert.Equal(["local only"], Badges("doomed"));

            // A remote-tracking line is the remote: none of this is about it.
            Assert.Empty(Badges("origin/published"));

            window.Close();
        });
    }

    [Fact]
    public void EveryBadge_SaysWhatItMeans()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenAsync(services, await BuildTrackingWorldAsync(services));

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 560 };
            window.Show();
            window.UpdateLayout();

            TreeView list = view.FindControl<TreeView>("BranchTree")
                ?? throw new InvalidOperationException("The branches page has no branch tree.");

            StackPanel[] badges = [.. list.GetVisualDescendants()
                .OfType<StackPanel>()
                .Where(badge => badge.Classes.Contains("badge") && badge.IsEffectivelyVisible)];

            Assert.NotEmpty(badges);
            Assert.All(badges, badge =>
            {
                Assert.NotNull(ToolTip.GetTip(badge));
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(badge)));
            });

            // The arrows are drawn beside their counts, and named for what they count.
            StackPanel ahead = Row(list, "tracked").GetVisualDescendants().OfType<StackPanel>()
                .First(badge => badge.Classes.Contains("badge") && badge.GetVisualDescendants().OfType<Icon>().Any(icon => icon.Kind == PhosphorIcon.ArrowUp));

            Assert.Equal("1 commit to push", ToolTip.GetTip(ahead));

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
            HistoryBranchViewModel branch = Assert.Single(row.Branches);
            Assert.Equal("unmerged", branch.Name);
            Assert.Contains("unmerged", branch.CheckoutHeader, StringComparison.Ordinal);
            Assert.True(branch.Commands.Checkout.CanExecute(branch));

            await branch.Commands.Checkout.ExecuteAsync(branch);

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
            Assert.Empty(row.Branches);
            Assert.DoesNotContain(row.MenuEntries, entry => entry.Header.Contains("merge source", StringComparison.Ordinal));

            // Creating one is always available: every commit can be branched from.
            Assert.True(row.Commands!.CreateBranchHere.CanExecute(row));
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

            HistoryBranchViewModel main = head.Branches.Single(branch => branch.IsLocal);
            Assert.Equal("main", main.Name);
            Assert.False(main.Commands.Checkout.CanExecute(main));
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

    /// <summary>
    /// Drives the whole gesture through the real input system: press, past the threshold, onto the
    /// target, release.
    /// </summary>
    private static void Drag(Window window, TreeViewItem from, TreeViewItem to)
    {
        Point start = Centre(from, window);
        Point end = Centre(to, window);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
    }

    private static Point Centre(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The row is not in the same tree as the window.");

    private static TreeViewItem Row(TreeView tree, string branch)
        => tree.GetRealizedTreeContainers()
            .OfType<TreeViewItem>()
            .First(container => container.DataContext is BranchRowViewModel row && row.FullName == branch);

    private static TreeViewItem Line(TreeView tree, BranchTreeNode node)
        => tree.GetRealizedTreeContainers()
            .OfType<TreeViewItem>()
            .First(container => ReferenceEquals(container.DataContext, node));

    /// <summary>
    /// Shows the page in a window, and returns the window and the page's tree.
    /// </summary>
    private static (Window Window, TreeView Tree) ShowTree(TestServices services, BranchesPageViewModel page)
    {
        BranchesPageView view = services.Get<BranchesPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1100, Height = 420 };
        window.Show();
        Settle(window);

        TreeView tree = view.FindControl<TreeView>("BranchTree")
            ?? throw new InvalidOperationException("The branches page has no branch tree.");

        return (window, tree);
    }

    /// <summary>
    /// A point half way down a line's own row — above its children, for an open node — and far enough
    /// from its item's left edge to be past the chevron.
    /// </summary>
    private static Point OnLine(TreeViewItem item, Window window, double x = 120)
    {
        Control row = item.GetVisualDescendants()
            .OfType<Grid>()
            .First(grid => grid.Classes.Contains("listrow") && ReferenceEquals(grid.DataContext, item.DataContext));

        Point middle = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The line is not in the window.");
        Point left = item.TranslatePoint(new Point(x, 0), window)
            ?? throw new InvalidOperationException("The line is not in the window.");

        return new Point(left.X, middle.Y);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    private static void Press(Window window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        Settle(window);
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

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
