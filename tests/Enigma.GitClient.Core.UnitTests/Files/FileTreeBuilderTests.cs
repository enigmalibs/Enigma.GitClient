using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Files;

public sealed class FileTreeBuilderTests
{
    private static ChangedFile File(
        string path,
        FileChangeKind kind = FileChangeKind.Modified,
        int added = 1,
        int removed = 1,
        bool conflicted = false)
        => new()
        {
            Path = path,
            ChangeKind = kind,
            AddedLines = added,
            RemovedLines = removed,
            IsConflicted = conflicted,
        };

    /// <summary>
    /// Renders a tree as indented text, which makes a failing assertion legible.
    /// </summary>
    private static string Render(IEnumerable<FileTreeNode> nodes, int depth = 0)
    {
        System.Text.StringBuilder builder = new();

        foreach (FileTreeNode node in nodes)
        {
            builder.Append(new string(' ', depth * 2))
                .Append(node.Name)
                .Append(node.IsDirectory ? "/" : string.Empty)
                .Append('\n');

            builder.Append(Render(node.Children, depth + 1));
        }

        return builder.ToString();
    }

    // ---------------------------------------------------------------- shape

    [Fact]
    public void Build_PlacesRootLevelFilesAtTheRoot()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build([File("README.md"), File("LICENSE.md")]);

        Assert.Equal(2, tree.Count);
        Assert.All(tree, node => Assert.False(node.IsDirectory));
        Assert.Equal(["LICENSE.md", "README.md"], tree.Select(node => node.Name));
    }

    [Fact]
    public void Build_NestsFilesUnderTheirDirectories()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("src/app.cs"),
            File("src/other.cs"),
            File("tests/app.tests.cs"),
        ]);

        Assert.Equal(
            """
            src/
              app.cs
              other.cs
            tests/
              app.tests.cs

            """.ReplaceLineEndings("\n"),
            Render(tree));
    }

    [Fact]
    public void Build_PutsDirectoriesBeforeFilesAtEveryLevel()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("zebra.txt"),
            File("alpha/one.txt"),
            File("beta.txt"),
            File("alpha/zulu/two.txt"),
        ]);

        Assert.Equal(["alpha", "beta.txt", "zebra.txt"], tree.Select(node => node.Name));

        FileTreeNode alpha = tree[0];
        Assert.Equal(["zulu", "one.txt"], alpha.Children.Select(node => node.Name));
    }

    [Fact]
    public void Build_SortsCaseInsensitively()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
            [File("Zebra.txt"), File("apple.txt"), File("Banana.txt")]);

        Assert.Equal(["apple.txt", "Banana.txt", "Zebra.txt"], tree.Select(node => node.Name));
    }

    [Fact]
    public void Build_KeepsAStableOrderForNamesDifferingOnlyInCase()
    {
        IReadOnlyList<FileTreeNode> first = FileTreeBuilder.Build([File("readme.md"), File("README.md")]);
        IReadOnlyList<FileTreeNode> second = FileTreeBuilder.Build([File("README.md"), File("readme.md")]);

        Assert.Equal(first.Select(node => node.Name), second.Select(node => node.Name));
    }

    [Fact]
    public void Build_ReturnsNothingForNoFiles()
        => Assert.Empty(FileTreeBuilder.Build([]));

    [Fact]
    public void Build_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => FileTreeBuilder.Build(null!));

    // ---------------------------------------------------------------- collapsing

    [Fact]
    public void Build_CollapsesAChainOfSingleChildDirectories()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
            [File("src/Enigma/GitClient/Core/Program.cs")]);

        FileTreeNode root = Assert.Single(tree);

        Assert.True(root.IsDirectory);
        Assert.Equal("src/Enigma/GitClient/Core", root.Name);
        Assert.Equal("src/Enigma/GitClient/Core", root.FullPath);
        Assert.Equal("Program.cs", Assert.Single(root.Children).Name);
    }

    [Fact]
    public void Build_DoesNotCollapseWhenTheOptionIsOff()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
            [File("src/Enigma/Core/Program.cs")],
            new FileTreeOptions { CollapseSingleChildDirectories = false });

        Assert.Equal(
            """
            src/
              Enigma/
                Core/
                  Program.cs

            """.ReplaceLineEndings("\n"),
            Render(tree));
    }

    [Fact]
    public void Build_StopsCollapsingWhereTheChainBranches()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("src/Enigma/Core/Program.cs"),
            File("src/Enigma/App/App.cs"),
        ]);

        FileTreeNode root = Assert.Single(tree);

        Assert.Equal("src/Enigma", root.Name);
        Assert.Equal(["App", "Core"], root.Children.Select(node => node.Name));
        Assert.Equal("App.cs", Assert.Single(root.Children[0].Children).Name);
    }

    [Fact]
    public void Build_DoesNotCollapseADirectoryWhoseOnlyChildIsAFile()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build([File("docs/README.md")]);

        FileTreeNode root = Assert.Single(tree);

        Assert.Equal("docs", root.Name);
        Assert.Equal("README.md", Assert.Single(root.Children).Name);
    }

    [Fact]
    public void Build_KeepsTheRealFullPathOfACollapsedDirectory()
    {
        FileTreeNode root = Assert.Single(FileTreeBuilder.Build([File("a/b/c/file.txt")]));

        Assert.Equal("a/b/c", root.Name);
        Assert.Equal("a/b/c", root.FullPath);
        Assert.Equal("a/b/c/file.txt", Assert.Single(root.Children).FullPath);
    }

    // ---------------------------------------------------------------- counts

    [Fact]
    public void Build_AggregatesCountsOnEveryDirectory()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("src/added.cs", FileChangeKind.Added, added: 10, removed: 0),
            File("src/deleted.cs", FileChangeKind.Deleted, added: 0, removed: 7),
            File("src/nested/modified.cs", FileChangeKind.Modified, added: 3, removed: 2),
            File("src/nested/renamed.cs", FileChangeKind.Renamed, added: 1, removed: 1),
            File("top.txt", FileChangeKind.Modified, added: 5, removed: 5),
        ]);

        FileTreeNode src = tree.Single(node => node.Name == "src");

        Assert.Equal(4, src.Counts.Total);
        Assert.Equal(1, src.Counts.Added);
        Assert.Equal(1, src.Counts.Deleted);
        Assert.Equal(1, src.Counts.Modified);
        Assert.Equal(1, src.Counts.Renamed);
        Assert.Equal(14, src.Counts.AddedLines);
        Assert.Equal(10, src.Counts.RemovedLines);

        FileTreeNode nested = src.Children.Single(node => node.Name == "nested");
        Assert.Equal(2, nested.Counts.Total);
        Assert.Equal(4, nested.Counts.AddedLines);

        FileTreeCounts total = FileTreeBuilder.Total(tree);
        Assert.Equal(5, total.Total);
        Assert.Equal(19, total.AddedLines);
        Assert.Equal(15, total.RemovedLines);
    }

    [Fact]
    public void Build_CountsConflicts()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("src/a.cs", conflicted: true),
            File("src/b.cs"),
        ]);

        Assert.Equal(1, tree.Single(node => node.Name == "src").Counts.Conflicted);
    }

    [Fact]
    public void Build_CountsACopyAsARename()
    {
        FileTreeNode file = Assert.Single(FileTreeBuilder.Build([File("copied.cs", FileChangeKind.Copied)]));

        Assert.Equal(1, file.Counts.Renamed);
        Assert.Equal(0, file.Counts.Modified);
    }

    [Fact]
    public void FileTreeCounts_AddCombinesTwoSets()
    {
        FileTreeCounts left = new(1, 2, 3, 4, 5, 6, 7);
        FileTreeCounts right = new(10, 20, 30, 40, 50, 60, 70);

        FileTreeCounts sum = FileTreeCounts.Add(left, right);

        Assert.Equal(11, sum.Added);
        Assert.Equal(22, sum.Modified);
        Assert.Equal(33, sum.Deleted);
        Assert.Equal(44, sum.Renamed);
        Assert.Equal(55, sum.Conflicted);
        Assert.Equal(66, sum.AddedLines);
        Assert.Equal(77, sum.RemovedLines);
        Assert.Equal(110, sum.Total);
    }

    // ---------------------------------------------------------------- paths & renames

    [Fact]
    public void Build_NormalisesWindowsSeparators()
    {
        FileTreeNode root = Assert.Single(FileTreeBuilder.Build([File(@"src\nested\file.cs")]));

        Assert.Equal("src/nested", root.Name);
        Assert.Equal("src/nested/file.cs", Assert.Single(root.Children).FullPath);
    }

    [Fact]
    public void Build_PlacesARenameUnderItsNewPathAndKeepsTheOldOne()
    {
        ChangedFile renamed = new()
        {
            Path = "src/new-name.cs",
            OldPath = "old/old-name.cs",
            ChangeKind = FileChangeKind.Renamed,
        };

        FileTreeNode file = Assert.Single(Assert.Single(FileTreeBuilder.Build([renamed])).Children);

        Assert.Equal("new-name.cs", file.Name);
        Assert.Equal("src/new-name.cs", file.FullPath);
        Assert.Equal("old/old-name.cs", file.Change!.OldPath);
        Assert.True(file.Change.IsRenamed);
    }

    [Fact]
    public void Build_KeepsOneRowForAPathThatAppearsTwice()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("src/file.cs") with { Staging = FileStagingState.Staged },
            File("src/file.cs") with { Staging = FileStagingState.Unstaged },
        ]);

        FileTreeNode file = Assert.Single(Assert.Single(tree).Children);

        Assert.Equal(FileStagingState.Unstaged, file.Change!.Staging);
    }

    [Fact]
    public void Build_IgnoresAnEmptyPath()
        => Assert.Empty(FileTreeBuilder.Build([File(string.Empty)]));

    // ---------------------------------------------------------------- navigation helpers

    [Fact]
    public void Flatten_ReturnsTheFilesInTreeOrder()
    {
        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
        [
            File("zebra.txt"),
            File("src/b.cs"),
            File("src/a.cs"),
            File("alpha.txt"),
        ]);

        Assert.Equal(
            ["src/a.cs", "src/b.cs", "alpha.txt", "zebra.txt"],
            FileTreeBuilder.Flatten(tree).Select(file => file.Path));
    }

    [Fact]
    public void Find_LocatesANodeByItsFullPath()
    {
        FileTreeNode root = Assert.Single(FileTreeBuilder.Build([File("src/nested/file.cs"), File("src/other.cs")]));

        Assert.Equal("file.cs", root.Find("src/nested/file.cs")?.Name);
        Assert.Equal("nested", root.Find("src/nested")?.Name);
        Assert.Null(root.Find("src/missing.cs"));
    }

    [Fact]
    public void DescendantsAndSelf_VisitsEveryNode()
    {
        FileTreeNode root = Assert.Single(FileTreeBuilder.Build([File("src/a.cs"), File("src/nested/b.cs")]));

        Assert.Equal(4, root.DescendantsAndSelf().Count());
    }

    [Fact]
    public void ToString_DistinguishesDirectoriesFromFiles()
    {
        FileTreeNode root = Assert.Single(FileTreeBuilder.Build([File("src/a.cs"), File("src/b.cs")]));

        Assert.Equal("src/ (2)", root.ToString());
        Assert.Equal("a.cs", root.Children[0].ToString());
    }

    // ---------------------------------------------------------------- ChangedFile

    [Theory]
    [InlineData("README.md", "README.md", "")]
    [InlineData("src/app.cs", "app.cs", "src")]
    [InlineData("a/b/c/deep.cs", "deep.cs", "a/b/c")]
    public void ChangedFile_SplitsItsNameAndDirectory(string path, string name, string directory)
    {
        ChangedFile file = File(path);

        Assert.Equal(name, file.Name);
        Assert.Equal(directory, file.DirectoryPath);
    }

    [Theory]
    [InlineData(@"src\app.cs", "src/app.cs")]
    [InlineData("/leading/slash.cs", "leading/slash.cs")]
    [InlineData("already/fine.cs", "already/fine.cs")]
    public void ChangedFile_NormalisesPaths(string input, string expected)
        => Assert.Equal(expected, ChangedFile.NormalisePath(input));

    [Fact]
    public void ChangedFile_IsRenamedOnlyWhenThePathActuallyChanged()
    {
        Assert.False(File("a.cs").IsRenamed);
        Assert.False((File("a.cs") with { ChangeKind = FileChangeKind.Renamed, OldPath = "a.cs" }).IsRenamed);
        Assert.True((File("b.cs") with { ChangeKind = FileChangeKind.Renamed, OldPath = "a.cs" }).IsRenamed);
        Assert.True((File("b.cs") with { ChangeKind = FileChangeKind.Copied, OldPath = "a.cs" }).IsRenamed);
    }

    [Fact]
    public void ChangedFile_SummarisesAParsedPatch()
    {
        const string patchText =
            """
            diff --git a/src/app.cs b/src/app.cs
            index 1..2 100644
            --- a/src/app.cs
            +++ b/src/app.cs
            @@ -1,2 +1,3 @@
             one
            -two
            +TWO
            +three
            """;

        FilePatch patch = Assert.Single(UnifiedDiffParser.Parse(patchText).Files);
        ChangedFile file = ChangedFile.FromPatch(patch, FileStagingState.Staged);

        Assert.Equal("src/app.cs", file.Path);
        Assert.Equal(FileChangeKind.Modified, file.ChangeKind);
        Assert.Equal(FileStagingState.Staged, file.Staging);
        Assert.Equal(2, file.AddedLines);
        Assert.Equal(1, file.RemovedLines);
        Assert.False(file.IsBinary);
    }

    [Fact]
    public void ChangedFile_SummarisesABinaryPatch()
    {
        const string patchText =
            """
            diff --git a/blob.bin b/blob.bin
            index 1..2 100644
            Binary files a/blob.bin and b/blob.bin differ
            """;

        ChangedFile file = ChangedFile.FromPatch(Assert.Single(UnifiedDiffParser.Parse(patchText).Files));

        Assert.True(file.IsBinary);
        Assert.Equal(0, file.AddedLines);
    }

    [Fact]
    public void ChangedFile_ToStringSummarisesTheChange()
        => Assert.Equal("Added src/new.cs", (File("src/new.cs", FileChangeKind.Added)).ToString());

    [Fact]
    public void FileTreeOptions_CollapsesByDefault()
        => Assert.True(FileTreeOptions.Default.CollapseSingleChildDirectories);

    // ---------------------------------------------------------------- scale

    [Fact]
    public void Build_HandlesAWideAndDeepTree()
    {
        List<ChangedFile> files = [];

        for (int directory = 0; directory < 200; directory++)
        {
            for (int file = 0; file < 50; file++)
            {
                files.Add(File(
                    $"src/module{directory.ToString(System.Globalization.CultureInfo.InvariantCulture)}/"
                    + $"file{file.ToString(System.Globalization.CultureInfo.InvariantCulture)}.cs"));
            }
        }

        IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(files);

        FileTreeNode root = Assert.Single(tree);
        Assert.Equal("src", root.Name);
        Assert.Equal(200, root.Children.Count);
        Assert.Equal(10_000, root.Counts.Total);
        Assert.Equal(10_000, FileTreeBuilder.Flatten(tree).Count);
    }
}
