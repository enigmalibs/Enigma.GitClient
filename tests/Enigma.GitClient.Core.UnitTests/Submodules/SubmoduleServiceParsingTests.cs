using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Submodules;

/// <summary>
/// The submodule service's own readings of git's output — the pins a diff moved, the gitlinks of a
/// tree, the message of a failed update — and the sentences an update's result puts in front of a user.
/// </summary>
public sealed class SubmoduleServiceParsingTests
{
    private const string A = "7235afd7e518fe877ee9b4d4a4e724fcdca80e87";
    private const string B = "818da6e941c86b09ad1bb3b4a83bc0c98777ce4e";
    private const string Zeros = "0000000000000000000000000000000000000000";

    [Fact]
    public void ParseChangedGitlinks_KeepsOnlyTheEntriesWhereEitherSideIsASubmodule()
    {
        string output =
            $":160000 160000 {A} {B} M\0libs/gamma\0" +
            $":100644 100644 {A} {B} M\0README.md\0" +
            $":000000 160000 {Zeros} {B} A\0libs/new one\0" +
            $":160000 000000 {A} {Zeros} D\0libs/gone\0" +
            $":100644 160000 {A} {B} T\0vendor/was-a-file\0";

        Assert.Equal(
            ["libs/gamma", "libs/new one", "libs/gone", "vendor/was-a-file"],
            SubmoduleService.ParseChangedGitlinks(output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseChangedGitlinks_ReadsNothingFromNothing(string? output)
        => Assert.Empty(SubmoduleService.ParseChangedGitlinks(output));

    [Fact]
    public void ParseGitlinksInTree_KeepsTheSubmodules()
    {
        string output =
            $"100644 blob {A}\t.gitmodules\0" +
            $"160000 commit {B}\tlibs/alpha\0" +
            $"100644 blob {A}\tREADME.md\0" +
            $"160000 commit {A}\tlibs/beta lib\0";

        Assert.Equal(["libs/alpha", "libs/beta lib"], SubmoduleService.ParseGitlinksInTree(output));
    }

    [Fact]
    public void SummariseFailure_KeepsGitsErrorsNotItsProgress()
    {
        string standardError =
            "Cloning into 'C:/work/libs/delta'...\r" +
            "Receiving objects:  50% (1/2)\r" +
            "fatal: repository 'https://user:secret@example.com/delta.git/' not found\n" +
            "fatal: clone of 'https://example.com/delta.git' into submodule path 'libs/delta' failed\n";

        string message = SubmoduleService.SummariseFailure(standardError);

        Assert.DoesNotContain("secret", message, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Receiving", message, System.StringComparison.Ordinal);
        Assert.Contains("not found", message, System.StringComparison.Ordinal);
        Assert.Contains("clone of", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SummariseFailure_FallsBackOnTheLastLine()
    {
        Assert.Equal("Something odd", SubmoduleService.SummariseFailure("Cloning...\nSomething odd\n"));
        Assert.Equal("git could not update it.", SubmoduleService.SummariseFailure("  "));
    }

    [Theory]
    [InlineData(SubmoduleSkipReason.UncommittedChanges, "libs/gamma has uncommitted changes.")]
    [InlineData(SubmoduleSkipReason.CommitOnNoBranch, "libs/gamma is on 7235afd, a commit no branch or tag contains; moving it would leave that commit behind.")]
    [InlineData(SubmoduleSkipReason.Conflicted, "libs/gamma has a merge conflict to resolve first.")]
    [InlineData(SubmoduleSkipReason.NotASubmodule, "libs/gamma is not a submodule listed in .gitmodules.")]
    public void SubmoduleSkip_SaysWhyTheSubmoduleWasLeft(SubmoduleSkipReason reason, string expected)
        => Assert.Equal(expected, new SubmoduleSkip("libs/gamma", reason, A).Message);

    [Fact]
    public void SubmoduleUpdateProgress_NamesTheSubmoduleAndTheTransferStep()
    {
        Assert.Equal("Updating submodule libs/delta", SubmoduleUpdateProgress.Starting("libs/delta").Description);

        SyncProgress receiving = SyncProgressParser.Parse("Receiving objects:  50% (1/2)")!;

        Assert.Equal(
            "Updating submodule libs/delta — Receiving objects",
            new SubmoduleUpdateProgress("libs/delta", receiving).Description);
    }

    [Fact]
    public void SubmoduleUpdateResult_HasProblemsOnlyWhenSomethingWasSkippedOrFailed()
    {
        Assert.False(SubmoduleUpdateResult.Nothing.HasProblems);
        Assert.False(new SubmoduleUpdateResult(["libs/a"], [], []).HasProblems);
        Assert.True(new SubmoduleUpdateResult([], [new SubmoduleSkip("libs/a", SubmoduleSkipReason.Conflicted)], []).HasProblems);
        Assert.True(new SubmoduleUpdateResult([], [], [new SubmoduleFailure("libs/a", "fatal")]).HasProblems);
    }
}
