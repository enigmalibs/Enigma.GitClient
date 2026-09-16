using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Scans the shipped source for any argument vector that would invoke the forbidden verb.
    /// </summary>
    /// <remarks>
    /// The command factory refuses it at runtime, which is the real guard. This is the second one:
    /// a quoted <c>"rebase"</c> anywhere outside the refusal itself means somebody wrote the
    /// invocation, and a runtime refusal a user never triggers is a failure nobody sees until a
    /// user does.
    /// </remarks>
    [Fact]
    public void NoShippedCodeEverAsksGitToRebase()
    {
        System.IO.DirectoryInfo? root = new(AppContext.BaseDirectory);

        while (root is not null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "Enigma.GitClient.slnx")))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            Assert.Skip("The solution root is not reachable from the test output directory.");
            return;
        }

        string source = System.IO.Path.Combine(root.FullName, "src");
        List<string> offenders = [];

        foreach (string file in System.IO.Directory.EnumerateFiles(source, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            if (string.Equals(System.IO.Path.GetFileName(file), "ForbiddenGitOperations.cs", StringComparison.Ordinal))
            {
                // The refusal itself has to name what it refuses.
                continue;
            }

            string text = System.IO.File.ReadAllText(file);

            if (text.Contains("\"rebase\"", StringComparison.Ordinal))
            {
                offenders.Add(System.IO.Path.GetRelativePath(root.FullName, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"these files name \"rebase\" as a git argument: {string.Join(", ", offenders)}");
    }
}
