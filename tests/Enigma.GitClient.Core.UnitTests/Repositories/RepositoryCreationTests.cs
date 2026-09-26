using System;
using System.IO;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Repositories;

public sealed class CloneRequestTests
{
    [Theory]
    [InlineData("https://github.com/owner/repository.git", "repository")]
    [InlineData("https://github.com/owner/repository", "repository")]
    [InlineData("https://github.com/owner/repository/", "repository")]
    [InlineData("git@github.com:owner/repository.git", "repository")]
    [InlineData("ssh://git@gitlab.example.com/group/subgroup/project.git", "project")]
    [InlineData("/home/user/projects/local-repo", "local-repo")]
    [InlineData("https://dev.azure.com/org/project/_git/Repo.Name", "Repo.Name")]
    [InlineData("", "repository")]
    [InlineData("   ", "repository")]
    public void DeriveDirectoryName_MatchesWhatGitWouldPick(string url, string expected)
        => Assert.Equal(expected, CloneRequest.DeriveDirectoryName(url));

    [Fact]
    public void TargetPath_UsesTheExplicitNameWhenGiven()
    {
        CloneRequest request = new()
        {
            Url = "https://github.com/owner/repository.git",
            ParentDirectory = OperatingSystem.IsWindows() ? @"C:\src" : "/src",
            DirectoryName = "custom-name",
        };

        Assert.Equal(Path.Combine(request.ParentDirectory, "custom-name"), request.TargetPath);
    }

    [Fact]
    public void TargetPath_FallsBackToTheDerivedName()
    {
        CloneRequest request = new()
        {
            Url = "https://github.com/owner/repository.git",
            ParentDirectory = OperatingSystem.IsWindows() ? @"C:\src" : "/src",
        };

        Assert.Equal(Path.Combine(request.ParentDirectory, "repository"), request.TargetPath);
    }

    [Fact]
    public void RemoteName_DefaultsToOrigin()
        => Assert.Equal(
            "origin",
            new CloneRequest { Url = "https://example.com/r.git", ParentDirectory = "/src" }.RemoteName);
}

public sealed class RemoteUrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com/owner/repository.git")]
    [InlineData("http://internal.example.com/repo.git")]
    [InlineData("ssh://git@github.com/owner/repository.git")]
    [InlineData("git://example.com/repo.git")]
    [InlineData("git@github.com:owner/repository.git")]
    [InlineData("gitlab.example.com:group/project.git")]
    public void Validate_AcceptsEveryShapeGitClonesFrom(string url)
    {
        RemoteUrlValidation validation = RemoteUrlValidator.Validate(url);

        Assert.True(validation.IsValid, validation.Message);
        Assert.Equal(string.Empty, validation.Message);
    }

    [Fact]
    public void Validate_AcceptsAnExistingLocalDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "enigma-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            Assert.True(RemoteUrlValidator.Validate(directory).IsValid);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("", "Enter a repository URL")]
    [InlineData("   ", "Enter a repository URL")]
    [InlineData("--upload-pack=evil", "cannot start with '-'")]
    [InlineData("telnet://example.com/repo", "not a protocol git can clone from")]
    public void Validate_RejectsWhatGitCannotUse(string url, string expectedFragment)
    {
        RemoteUrlValidation validation = RemoteUrlValidator.Validate(url);

        Assert.False(validation.IsValid);
        Assert.Contains(expectedFragment, validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsALocalPathThatDoesNotExist()
    {
        RemoteUrlValidation validation = RemoteUrlValidator.Validate(
            Path.Combine(Path.GetTempPath(), "enigma-does-not-exist-" + Guid.NewGuid().ToString("N")));

        Assert.False(validation.IsValid);
        Assert.Contains("no such directory", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git", true)]
    [InlineData("gitlab.example.com:group/project.git", true)]
    [InlineData("host:path", true)]
    [InlineData("user@host:path", true)]
    [InlineData("C:/src/repo", false)]
    [InlineData("https://host/path", false)]
    [InlineData("ssh://git@host:22/path", false)]
    [InlineData("no-colon-here", false)]
    [InlineData("host:", false)]
    public void IsScpLike_RecognisesGitsScpSyntax(string value, bool expected)
        => Assert.Equal(expected, RemoteUrlValidator.IsScpLike(value));

    [Theory]
    [InlineData("/srv/git/repo", true)]
    [InlineData("./relative", true)]
    [InlineData("../sibling", true)]
    [InlineData("~/projects/repo", true)]
    [InlineData(@"C:\src\repo", true)]
    [InlineData(@"\\server\share\repo", true)]
    [InlineData("https://host/path", false)]
    [InlineData("git@host:path", false)]
    public void LooksLikeLocalPath_TellsAPathFromAUrl(string value, bool expected)
        => Assert.Equal(expected, RemoteUrlValidator.LooksLikeLocalPath(value));

    [Fact]
    public void Validate_RejectsAFileUrlPointingAtNothing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "enigma-missing-" + Guid.NewGuid().ToString("N"));

        RemoteUrlValidation validation = RemoteUrlValidator.Validate(new Uri(missing).AbsoluteUri);

        Assert.False(validation.IsValid);
        Assert.Contains("does not exist", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsAFileUrlPointingAtARealDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "enigma-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            Assert.True(RemoteUrlValidator.Validate(new Uri(directory).AbsoluteUri).IsValid);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Validate_TrimsSurroundingWhitespace()
        => Assert.Equal(
            "https://github.com/owner/repo.git",
            RemoteUrlValidator.Validate("  https://github.com/owner/repo.git  ").NormalisedUrl);
}

public sealed class CloneProgressParserTests
{
    [Theory]
    [InlineData("remote: Counting objects:  35% (7/20)", CloneStage.CountingObjects, 35)]
    [InlineData("remote: Enumerating objects: 137, done.", CloneStage.CountingObjects, null)]
    [InlineData("remote: Compressing objects: 100% (50/50), done.", CloneStage.CompressingObjects, 100)]
    [InlineData("Receiving objects:  73% (100/137), 1.20 MiB | 2.00 MiB/s", CloneStage.ReceivingObjects, 73)]
    [InlineData("Resolving deltas:   8% (4/50)", CloneStage.ResolvingDeltas, 8)]
    [InlineData("Updating files:  50% (10/20)", CloneStage.UpdatingFiles, 50)]
    [InlineData("Checking out files: 100% (20/20), done.", CloneStage.UpdatingFiles, 100)]
    public void Parse_ReadsEveryStageGitReports(string line, CloneStage stage, int? percentage)
    {
        CloneProgress? progress = CloneProgressParser.Parse(line);

        Assert.NotNull(progress);
        Assert.Equal(stage, progress!.Stage);
        Assert.Equal(percentage, progress.Percentage);
    }

    [Fact]
    public void Parse_StripsTheRemotePrefixFromTheMessage()
        => Assert.Equal(
            "Counting objects:  35% (7/20)",
            CloneProgressParser.Parse("remote: Counting objects:  35% (7/20)")!.Message);

    [Fact]
    public void Parse_KeepsAnUnrecognisedLineAsAMessage()
    {
        CloneProgress? progress = CloneProgressParser.Parse("Cloning into 'repository'...");

        Assert.NotNull(progress);
        Assert.Equal(CloneStage.Starting, progress!.Stage);
        Assert.Null(progress.Percentage);
        Assert.Equal("Cloning into 'repository'...", progress.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("remote:")]
    public void Parse_IgnoresAnEmptyLine(string? line)
        => Assert.Null(CloneProgressParser.Parse(line));

    [Theory]
    [InlineData("Receiving objects:  73%", 73)]
    [InlineData("100% done", 100)]
    [InlineData("  0% (0/1)", 0)]
    [InlineData("no percentage here", null)]
    [InlineData("% leading", null)]
    [InlineData("Receiving objects: 150%", 100)]
    public void ExtractPercentage_ReadsTheNumberBeforeTheSign(string text, int? expected)
        => Assert.Equal(expected, CloneProgressParser.ExtractPercentage(text));

    [Fact]
    public void StageDescription_NamesEveryStage()
    {
        foreach (CloneStage stage in Enum.GetValues<CloneStage>())
        {
            Assert.NotEqual(string.Empty, new CloneProgress(stage, null, string.Empty).StageDescription);
        }
    }

    [Fact]
    public void Starting_IsTheReportBeforeGitSaysAnything()
    {
        Assert.Equal(CloneStage.Starting, CloneProgress.Starting.Stage);
        Assert.Null(CloneProgress.Starting.Percentage);
    }
}
