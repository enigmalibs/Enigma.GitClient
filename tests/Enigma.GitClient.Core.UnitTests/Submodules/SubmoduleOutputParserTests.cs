using System.Collections.Generic;
using Enigma.GitClient.Core.Submodules;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Submodules;

/// <summary>
/// Reads <c>git ls-files --stage -z</c> and <c>git config -f .gitmodules -z --get-regexp</c> the way git
/// prints them — the submodule entries among the files, the stages of a conflict, and paths and names
/// holding spaces, dots, tabs and parentheses of their own.
/// </summary>
public sealed class SubmoduleOutputParserTests
{
    private const string A = "7235afd7e518fe877ee9b4d4a4e724fcdca80e87";
    private const string B = "818da6e941c86b09ad1bb3b4a83bc0c98777ce4e";

    [Fact]
    public void ParseGitlinks_KeepsTheSubmodulesAmongTheFiles()
    {
        string output =
            $"100644 {B} 0\t.gitmodules\0" +
            $"160000 {A} 0\tlibs/Common\0" +
            $"100755 {B} 0\tbuild.sh\0" +
            $"160000 {B} 0\tlibs/beta lib\0";

        Assert.Equal(
            [new GitlinkEntry("libs/Common", A, 0), new GitlinkEntry("libs/beta lib", B, 0)],
            SubmoduleOutputParser.ParseGitlinks(output));
    }

    [Fact]
    public void ParseGitlinks_ReadsTheStagesOfAConflict()
    {
        string output =
            $"160000 {A} 1\tlibs/Common\0" +
            $"160000 {A} 2\tlibs/Common\0" +
            $"160000 {B} 3\tlibs/Common\0";

        Assert.Equal(
            [new GitlinkEntry("libs/Common", A, 1), new GitlinkEntry("libs/Common", A, 2), new GitlinkEntry("libs/Common", B, 3)],
            SubmoduleOutputParser.ParseGitlinks(output));
    }

    [Theory]
    [InlineData("third party/json (x)")]
    [InlineData("tab\there")]
    [InlineData("v1.2 (2019)/lib")]
    public void ParseGitlinks_KeepsThePathWhole(string path)
        => Assert.Equal(path, Assert.Single(SubmoduleOutputParser.ParseGitlinks($"160000 {A} 0\t{path}\0")).Path);

    [Fact]
    public void ParseGitlinks_ReadsASha256Commit()
    {
        string sha = new('a', 64);

        Assert.Equal(sha, Assert.Single(SubmoduleOutputParser.ParseGitlinks($"160000 {sha} 0\tlibs/Common\0")).Sha);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("160000 notasha 0\tlibs/Common\0")]
    [InlineData("160000 7235afd7e518fe877ee9b4d4a4e724fcdca80e87 7\tlibs/Common\0")]
    [InlineData("160000 7235afd7e518fe877ee9b4d4a4e724fcdca80e87 0\t\0")]
    [InlineData("160000 7235afd7e518fe877ee9b4d4a4e724fcdca80e87 0 libs/Common\0")]
    [InlineData("1600001 7235afd7e518fe877ee9b4d4a4e724fcdca80e87 0\tlibs/Common\0")]
    public void ParseGitlinks_IgnoresARecordWithoutTheShape(string? output)
        => Assert.Empty(SubmoduleOutputParser.ParseGitlinks(output));

    [Fact]
    public void ParseGitModules_ReadsNamesHoldingDotsAndSpaces()
    {
        string output =
            "submodule.libs/alpha.path\nlibs/alpha\0" +
            "submodule.libs/alpha.url\n../alpha\0" +
            "submodule.libs/beta lib.path\nlibs/beta lib\0" +
            "submodule.libs/beta lib.url\nhttps://user:secret@example.com/beta.git\0" +
            "submodule.v1.2.path\nvendor/v1.2/\0" +
            "submodule.v1.2.branch\nmain\0";

        IReadOnlyList<GitModulesEntry> entries = SubmoduleOutputParser.ParseGitModules(output);

        Assert.Equal(
            [
                new GitModulesEntry("libs/alpha", "libs/alpha", "../alpha"),
                new GitModulesEntry("libs/beta lib", "libs/beta lib", "https://user:secret@example.com/beta.git"),
                new GitModulesEntry("v1.2", "vendor/v1.2", string.Empty),
            ],
            entries);
    }

    [Fact]
    public void ParseGitModules_LeavesOutASubmoduleWithoutAPath()
    {
        IReadOnlyList<GitModulesEntry> entries =
            SubmoduleOutputParser.ParseGitModules("submodule.lost.url\nhttps://example.com/lost.git\0");

        Assert.Empty(entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("core.bare\nfalse\0")]
    [InlineData("submodule.path\nlibs/x\0")]
    public void ParseGitModules_ReadsNothingFromWhatIsNotADeclaration(string? output)
        => Assert.Empty(SubmoduleOutputParser.ParseGitModules(output));

    [Theory]
    [InlineData("libs\\Common\\", "libs/Common")]
    [InlineData("libs/Common/", "libs/Common")]
    [InlineData("libs/Common", "libs/Common")]
    public void NormalisePath_WritesThePathAsGitPrintsIt(string path, string expected)
        => Assert.Equal(expected, SubmoduleOutputParser.NormalisePath(path));

    [Theory]
    [InlineData("7235afd7e518fe877ee9b4d4a4e724fcdca80e87", true)]
    [InlineData("7235AFD7E518FE877EE9B4D4A4E724FCDCA80E87", true)]
    [InlineData("7235afd", false)]
    [InlineData("-235afd7e518fe877ee9b4d4a4e724fcdca80e87", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsObjectName_AcceptsOnlyAWholeObjectName(string? text, bool expected)
        => Assert.Equal(expected, SubmoduleOutputParser.IsObjectName(text));
}
