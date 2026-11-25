using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Stashes;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// How the history draws the stash (CODE-REVIEW-6E56 SOLID-03, the folding out of the history page):
/// an entry is one line off the commit it was made on, and the commits git records it with are no line
/// of their own unless a reference points at one.
/// </summary>
public sealed class StashFoldingTests
{
    private static readonly GitSignature Someone = new("Someone", "someone@example.com", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly RefDecorationIndex NoRefs = RefDecorationIndex.Build(Array.Empty<GitRef>());

    private static GitCommit Commit(string sha, params string[] parents) => new(sha, parents, Someone, Someone, sha, string.Empty);

    private static StashEntry Entry(string sha, int index = 0) => new(index, sha, "WIP on main", "main", Someone.When);

    // The stash entry "s" was made on "b": a merge of b, its index "i" and its untracked files "u".
    private static IReadOnlyList<GitCommit> Page() =>
    [
        Commit("c", "b"),
        Commit("s", "b", "i", "u"),
        Commit("i", "b"),
        Commit("u"),
        Commit("b", "a"),
        Commit("a"),
    ];

    [Fact]
    public void WithNoStash_ThePageIsDrawnAsItIs()
    {
        StashFolding folding = new();
        IReadOnlyList<GitCommit> page = Page();

        Assert.Same(page, folding.Fold(page, NoRefs));
    }

    [Fact]
    public void AnEntry_IsOneLineOffTheCommitItWasMadeOn_AndItsHelpersAreNoLines()
    {
        StashFolding folding = new();
        folding.Use([Entry("s")]);

        IReadOnlyList<GitCommit> folded = folding.Fold(Page(), NoRefs);

        Assert.Equal(["c", "s", "b", "a"], folded.Select(commit => commit.Sha));
        Assert.Equal(["b"], folded.Single(commit => commit.Sha == "s").ParentShas);
        Assert.Equal("s", folding.EntryAt("s")?.Sha);
        Assert.Null(folding.EntryAt("c"));
    }

    [Fact]
    public void AHelperFoundOnOnePage_IsFoldedOnTheNext_UntilTheHistoryIsReadFromTheTop()
    {
        StashFolding folding = new();
        folding.Use([Entry("s")]);

        folding.Fold([Commit("s", "b", "i")], NoRefs);

        Assert.Empty(folding.Fold([Commit("i", "b")], NoRefs));

        folding.ForgetHelpers();
        folding.Use([]);

        Assert.Equal(["i"], folding.Fold([Commit("i", "b")], NoRefs).Select(commit => commit.Sha));
    }

    [Fact]
    public void AHelperAReferencePointsAt_IsKept_ForItsBadge()
    {
        StashFolding folding = new();
        folding.Use([Entry("s")]);
        RefDecorationIndex refs = RefDecorationIndex.Build([new GitTag("refs/tags/odd", "i", null, null, string.Empty, Someone.When)]);

        IReadOnlyList<GitCommit> folded = folding.Fold(Page(), refs);

        Assert.Contains(folded, commit => commit.Sha == "i");
        Assert.DoesNotContain(folded, commit => commit.Sha == "u");
    }

    [Fact]
    public void HasMoved_SaysWhetherTheStashIsNotTheOneDrawn()
    {
        StashFolding folding = new();
        folding.Use([Entry("s", 0), Entry("t", 1)]);

        Assert.False(folding.HasMoved([Entry("s", 0), Entry("t", 1)]));
        Assert.True(folding.HasMoved([Entry("t", 0)]));
        Assert.True(folding.HasMoved([Entry("n", 0), Entry("s", 1), Entry("t", 2)]));
    }
}
