using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// How the branches page's tree is built from the branches: its top-level nodes, its folders and their
/// merged chains, its order, and which nodes start open.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BranchTreeBuilderTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly HeadlessAvaloniaFixture _fixture;

    public BranchTreeBuilderTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the top level

    [Fact]
    public void LocalComesFirst_ThenOneNodePerRemote_Alphabetically()
        => WithPage(page =>
        {
            IReadOnlyList<BranchGroupViewModel> tree = Build(
                page,
                local: ["main"],
                remote: ["upstream/main", "backup/main", "origin/main", "Mirror/main"]);

            Assert.Equal(["Local", "backup", "Mirror", "origin", "upstream"], tree.Select(group => group.Title));
            Assert.False(tree[0].IsRemote);
            Assert.All(tree.Skip(1), group => Assert.True(group.IsRemote));
            Assert.Equal(["local", "remote:backup", "remote:Mirror", "remote:origin", "remote:upstream"], tree.Select(group => group.Key));
        });

    [Fact]
    public void ANodeWithNothingUnderIt_IsNotThere()
        => WithPage(page =>
        {
            IReadOnlyList<BranchGroupViewModel> tree = Build(page, local: [], remote: ["origin/main"]);

            Assert.Equal(["origin"], tree.Select(group => group.Title));
        });

    // ---------------------------------------------------------------- folders

    [Fact]
    public void EverySlashSegment_IsAFolder()
        => WithPage(page =>
        {
            BranchGroupViewModel local = Build(page, local: ["feature/tree", "feature/watcher", "main"], remote: [])[0];

            Assert.Equal(["feature/", "main"], Shape(local.Children));

            BranchFolderViewModel feature = Assert.IsType<BranchFolderViewModel>(local.Children[0]);
            Assert.Equal("local/feature", feature.Key);
            Assert.Equal(["tree", "watcher"], Shape(feature.Children));
            Assert.Equal("2", feature.Count);
        });

    [Fact]
    public void AChainOfFoldersThatEachHoldOneFolder_IsOneNode()
        => WithPage(page =>
        {
            BranchGroupViewModel local = Build(
                page,
                local: ["vibe/2026-10-08/one", "vibe/2026-10-08/two", "a/b/c/leaf", "deep/one/x", "deep/two/y"],
                remote: [])[0];

            Assert.Equal(["a/b/c/", "deep/", "vibe/2026-10-08/"], Shape(local.Children));

            BranchFolderViewModel vibe = (BranchFolderViewModel)local.Children[2];
            Assert.Equal("local/vibe/2026-10-08", vibe.Key);
            Assert.Equal(["one", "two"], Shape(vibe.Children));

            // A folder holding one branch is still a folder: only a chain of folders is merged.
            Assert.Equal(["leaf"], Shape(local.Children[0].Children));

            // Two folders under one: nothing to merge.
            Assert.Equal(["one/", "two/"], Shape(local.Children[1].Children));
        });

    [Fact]
    public void ARemotesBranches_AreFolderedWithoutTheRemotesName()
        => WithPage(page =>
        {
            BranchGroupViewModel origin = Build(page, local: [], remote: ["origin/feature/x", "origin/main"])[0];

            Assert.Equal(["feature/", "main"], Shape(origin.Children));
            Assert.Equal("remote:origin/feature", origin.Children[0].Key);
            Assert.Equal("origin/feature/x", Assert.Single(origin.Rows, row => row.Label == "x").FullName);
        });

    // ---------------------------------------------------------------- order

    [Fact]
    public void Folders_ComeFirst_ByName_WhicheverWayTheBranchesRun()
        => WithPage(page =>
        {
            string[] local = ["zeta", "alpha/x", "Beta/y", "main"];

            Assert.Equal(
                ["alpha/", "Beta/", "main", "zeta"],
                Shape(Build(page, local, [], RefSortKey.Name, SortDirection.Ascending)[0].Children));

            Assert.Equal(
                ["alpha/", "Beta/", "zeta", "main"],
                Shape(Build(page, local, [], RefSortKey.Name, SortDirection.Descending)[0].Children));
        });

    [Fact]
    public void Branches_RunInThePagesOrder_ByDate()
        => WithPage(page =>
        {
            BranchRowViewModel old = Row(page, "refs/heads/old", Monday);
            BranchRowViewModel young = Row(page, "refs/heads/young", Monday.AddDays(2));
            BranchRowViewModel middle = Row(page, "refs/heads/middle", Monday.AddDays(1));

            IReadOnlyList<BranchGroupViewModel> newestFirst = BranchTreeBuilder.Build(
                [old, young, middle], [], RefSortKey.Date, SortDirection.Descending, _ => null, expandAll: false, expansionChanged: null);

            Assert.Equal(["young", "middle", "old"], Shape(newestFirst[0].Children));

            IReadOnlyList<BranchGroupViewModel> oldestFirst = BranchTreeBuilder.Build(
                [old, young, middle], [], RefSortKey.Date, SortDirection.Ascending, _ => null, expandAll: false, expansionChanged: null);

            Assert.Equal(["old", "middle", "young"], Shape(oldestFirst[0].Children));

            // The group's Rows follow the tree: what a reader scrolls past, in order.
            Assert.Equal(["old", "middle", "young"], oldestFirst[0].Rows.Select(row => row.Label));
        });

    // ---------------------------------------------------------------- open and closed

    [Fact]
    public void ATopLevelNodeStartsOpen_AndAFolderClosed()
        => WithPage(page =>
        {
            BranchGroupViewModel local = Build(page, local: ["feature/x", "main"], remote: [])[0];

            Assert.True(local.IsExpanded);
            Assert.False(local.Children[0].IsExpanded);
        });

    [Fact]
    public void WhatWasRemembered_OpensOrClosesANode()
        => WithPage(page =>
        {
            Dictionary<string, bool> remembered = new() { ["local"] = false, ["local/feature"] = true };

            BranchGroupViewModel local = BranchTreeBuilder.Build(
                Rows(page, ["feature/x", "main"]), [], RefSortKey.Name, SortDirection.Ascending,
                key => remembered.TryGetValue(key, out bool open) ? open : null,
                expandAll: false,
                expansionChanged: null)[0];

            Assert.False(local.IsExpanded);
            Assert.True(local.Children[0].IsExpanded);
        });

    [Fact]
    public void WhileFiltering_EveryNodeIsOpen_WhateverWasRemembered()
        => WithPage(page =>
        {
            BranchGroupViewModel local = BranchTreeBuilder.Build(
                Rows(page, ["feature/deep/x"]), [], RefSortKey.Name, SortDirection.Ascending,
                _ => false,
                expandAll: true,
                expansionChanged: null)[0];

            Assert.True(local.IsExpanded);
            Assert.True(local.Children[0].IsExpanded);
        });

    [Fact]
    public void TheReaderOpeningANode_IsReported_ButBuildingTheTreeIsNot()
        => WithPage(page =>
        {
            List<string> reported = [];

            BranchGroupViewModel local = BranchTreeBuilder.Build(
                Rows(page, ["feature/x"]), [], RefSortKey.Name, SortDirection.Ascending,
                _ => null,
                expandAll: false,
                node => reported.Add($"{node.Key}={node.IsExpanded}"))[0];

            Assert.Empty(reported);

            local.Children[0].IsExpanded = true;
            local.IsExpanded = false;

            Assert.Equal(["local/feature=True", "local=False"], reported);
        });

    // ---------------------------------------------------------------- plumbing

    private void WithPage(Action<BranchesPageViewModel> test)
        => _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            test(services.Get<BranchesPageViewModel>());
        });

    private static IReadOnlyList<BranchGroupViewModel> Build(
        BranchesPageViewModel page,
        string[] local,
        string[] remote,
        RefSortKey key = RefSortKey.Name,
        SortDirection direction = SortDirection.Ascending)
        => BranchTreeBuilder.Build(
            Rows(page, local),
            [.. remote.Select(name => Row(page, $"refs/remotes/{name}", Monday))],
            key,
            direction,
            _ => null,
            expandAll: false,
            expansionChanged: null);

    private static List<BranchRowViewModel> Rows(BranchesPageViewModel page, string[] names)
        => [.. names.Select(name => Row(page, $"refs/heads/{name}", Monday))];

    private static BranchRowViewModel Row(BranchesPageViewModel page, string fullName, DateTimeOffset date)
        => new(page, new GitBranch(fullName, new string('a', 40), false, null, BranchTracking.None, GitSignature.Empty, date, "A subject"));

    /// <summary>The labels of some nodes, a folder's ending in a slash.</summary>
    private static string[] Shape(IEnumerable<BranchTreeNode> nodes)
        => [.. nodes.Select(node => node.IsFolder ? node.Label + "/" : node.Label)];
}
