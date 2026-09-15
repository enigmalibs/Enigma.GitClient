using System;
using Enigma.GitClient.Core.Git;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

/// <summary>
/// Rebase is a permanent product exclusion. These tests are what stops a later change from
/// reintroducing it, so they assert the ban from every angle a caller could approach it.
/// </summary>
public sealed class ForbiddenGitOperationsTests
{
    [Theory]
    [InlineData("rebase")]
    [InlineData("rebase --continue")]
    [InlineData("rebase -i HEAD~3")]
    [InlineData("rebase --onto main topic")]
    [InlineData("REBASE")]
    [InlineData("--no-pager -c color.ui=false rebase main")]
    [InlineData("-C /repo rebase")]
    public void EnsureAllowed_RejectsTheRebaseVerb(string commandLine)
    {
        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(() => ForbiddenGitOperations.EnsureAllowed(Split(commandLine)));

        Assert.Contains("rebase", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pull --rebase")]
    [InlineData("pull --rebase=interactive")]
    [InlineData("pull -r")]
    [InlineData("pull origin main --rebase")]
    public void EnsureAllowed_RejectsAPullThatWouldRebase(string commandLine)
        => Assert.Throws<NotSupportedException>(() => ForbiddenGitOperations.EnsureAllowed(Split(commandLine)));

    [Theory]
    [InlineData("-c pull.rebase=true pull")]
    [InlineData("-c pull.rebase pull")]
    [InlineData("-c pull.rebase=interactive pull")]
    [InlineData("-c branch.main.rebase=true pull")]
    public void EnsureAllowed_RejectsConfigurationThatWouldEnableRebase(string commandLine)
        => Assert.Throws<NotSupportedException>(() => ForbiddenGitOperations.EnsureAllowed(Split(commandLine)));

    [Theory]
    [InlineData("pull --no-rebase")]
    [InlineData("-c pull.rebase=false pull --no-rebase")]
    [InlineData("log --oneline")]
    [InlineData("merge --no-ff topic")]
    [InlineData("checkout -b rebase-experiments")]
    [InlineData("log --grep rebase")]
    [InlineData("branch rebase")]
    [InlineData("-C rebase status")]
    public void EnsureAllowed_AllowsEverythingElse(string commandLine)
        => ForbiddenGitOperations.EnsureAllowed(Split(commandLine));

    [Fact]
    public void ForbiddenVerbs_ContainsRebase()
        => Assert.Contains("rebase", ForbiddenGitOperations.ForbiddenVerbs);

    private static string[] Split(string commandLine)
        => commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
