using System;
using System.Collections.Generic;
using System.IO;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.Options;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

/// <summary>
/// How a program is found on the <c>PATH</c>, and how git is: never by a bare name, and never in the
/// current directory, which is often a repository somebody else wrote (SEC-01).
/// </summary>
public sealed class ExecutableSearchTests
{
    // Rooted on whichever platform the tests run on.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "enigma-search");

    private static string In(string folder, string file) => Path.Combine(Root, folder, file);

    private static string PathOf(params string[] entries) => string.Join(Path.PathSeparator, entries);

    [Fact]
    public void FindOnPath_ReturnsTheFirstDirectoryThatHasTheProgram()
    {
        HashSet<string> files = [In("second", "tool"), In("third", "tool")];

        string? found = ExecutableSearch.FindOnPath(
            "tool",
            PathOf(Path.Combine(Root, "first"), Path.Combine(Root, "second"), Path.Combine(Root, "third")),
            files.Contains);

        Assert.Equal(In("second", "tool"), found);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("bin")]
    [InlineData("./bin")]
    public void FindOnPath_NeverSearchesAnEntryThatIsNotFullyQualified(string entry)
    {
        // Whatever the relative entry would resolve to, the search does not look: "." is the current
        // directory under another name.
        Assert.Null(ExecutableSearch.FindOnPath("tool", entry, _ => true));
    }

    [Fact]
    public void FindOnPath_SkipsARelativeEntry_AndFindsTheProgramInTheNextOne()
    {
        string found = Assert.IsType<string>(ExecutableSearch.FindOnPath(
            "tool",
            PathOf(".", Path.Combine(Root, "bin")),
            _ => true));

        Assert.Equal(In("bin", "tool"), found);
        Assert.True(Path.IsPathFullyQualified(found));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FindOnPath_WithNoPath_FindsNothing(string? path)
        => Assert.Null(ExecutableSearch.FindOnPath("tool", path, _ => true));

    [Fact]
    public void FindOnPath_AcceptsAQuotedEntry()
    {
        string quoted = "\"" + Path.Combine(Root, "with space") + "\"";

        Assert.Equal(In("with space", "tool"), ExecutableSearch.FindOnPath("tool", quoted, _ => true));
    }

    [Fact]
    public void Resolve_WithNothingFound_ReturnsNull_NotTheBareName()
    {
        string? resolved = GitExecutable.Resolve(
            configuredPath: null,
            environment: _ => null,
            fileExists: _ => true,
            isWindows: OperatingSystem.IsWindows());

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_IgnoresAGitInARelativePathEntry()
    {
        string? resolved = GitExecutable.Resolve(
            configuredPath: null,
            environment: name => name == "PATH" ? "." : null,
            fileExists: _ => true,
            isWindows: false);

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_FindsGitOnTheFullyQualifiedPath()
    {
        string expected = In("git-bin", "git");

        string? resolved = GitExecutable.Resolve(
            configuredPath: null,
            environment: name => name == "PATH" ? PathOf(".", Path.Combine(Root, "git-bin")) : null,
            fileExists: path => path == expected,
            isWindows: false);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Resolve_OnWindows_PrefersTheUsualInstallation()
    {
        string programFiles = Path.Combine(Root, "Program Files");
        string expected = Path.Combine(programFiles, "Git", "cmd", "git.exe");

        string? resolved = GitExecutable.Resolve(
            configuredPath: null,
            environment: name => name switch
            {
                "ProgramFiles" => programFiles,
                "PATH" => Path.Combine(Root, "elsewhere"),
                _ => null,
            },
            fileExists: _ => true,
            isWindows: true);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Resolve_HonoursAConfiguredPathVerbatim()
    {
        string configured = In("custom", "git");

        Assert.Equal(configured, GitExecutable.Resolve(configured, _ => null, _ => false, isWindows: false));
    }

    [Fact]
    public void TheInstance_ResolvesTheConfiguredPath()
    {
        GitExecutable executable = new(Options.Create(new GitExecutableOptions { ExecutablePath = In("custom", "git") }));

        Assert.True(executable.TryResolve(out string? path));
        Assert.Equal(In("custom", "git"), path);
        Assert.Equal(In("custom", "git"), executable.Path);
    }
}
