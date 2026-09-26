using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.History;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.History;

/// <summary>
/// The command line the history is read with, where the refs it leaves out go.
/// </summary>
public sealed class CommitLogArgumentsTests
{
    [Fact]
    public void AnExcludedRef_GoesImmediatelyBeforeAll()
    {
        List<string> arguments = CommitLogReader.BuildArguments(
            new CommitLogQuery { ExcludedRefs = ["refs/heads/topic", "refs/remotes/origin/topic"] },
            take: 10);

        int all = arguments.IndexOf("--all");

        // git applies an --exclude to the next --all only: after it, it would leave nothing out.
        Assert.Equal(
            ["--exclude=refs/heads/topic", "--exclude=refs/remotes/origin/topic", "--all"],
            arguments.Skip(all - 2).Take(3));
        Assert.Equal("--", arguments[all + 1]);
    }

    [Fact]
    public void NothingExcluded_IsThePlainWalkOfEveryRef()
    {
        List<string> arguments = CommitLogReader.BuildArguments(new CommitLogQuery(), take: 10);

        Assert.Contains("--all", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--exclude", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("topic")]
    [InlineData("refs/")]
    [InlineData("heads/topic")]
    [InlineData("refs/heads/*")]
    [InlineData("refs/heads/t?pic")]
    [InlineData("refs/heads/[a]")]
    [InlineData("refs/heads/with space")]
    public void AnEntryThatIsNotOneFullRefName_IsLeftOut(string entry)
    {
        List<string> arguments = CommitLogReader.BuildArguments(
            new CommitLogQuery { ExcludedRefs = [entry, "refs/heads/kept"] },
            take: 10);

        Assert.Equal(["--exclude=refs/heads/kept"], arguments.Where(argument => argument.StartsWith("--exclude", System.StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(CommitLogScope.Head)]
    [InlineData(CommitLogScope.Revision)]
    public void AnotherScope_IgnoresTheList(CommitLogScope scope)
    {
        List<string> arguments = CommitLogReader.BuildArguments(
            new CommitLogQuery { Scope = scope, Revision = "topic", ExcludedRefs = ["refs/heads/feature"] },
            take: 10);

        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--exclude", System.StringComparison.Ordinal));
    }

    [Fact]
    public void TheCount_LeavesOutTheSameRefs()
    {
        List<string> arguments = CommitLogReader.BuildArguments(
            new CommitLogQuery { ExcludedRefs = ["refs/heads/feature"] },
            take: null,
            countOnly: true);

        Assert.Equal("rev-list", arguments[0]);
        Assert.Equal(arguments.IndexOf("--all") - 1, arguments.IndexOf("--exclude=refs/heads/feature"));
    }
}
