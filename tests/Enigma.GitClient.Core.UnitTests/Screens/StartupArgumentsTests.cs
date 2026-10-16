using System.Globalization;
using Enigma.GitClient.Core.Screens;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Screens;

/// <summary>
/// Covers the command line another instance is started with: the repository it opens, and the screen of
/// the window that started it — and that a command line typed by hand still opens what it names.
/// </summary>
public sealed class StartupArgumentsTests
{
    [Fact]
    public void Parse_APathAlone_OpensItWhereTheApplicationWasLastUsed()
    {
        StartupArguments startup = StartupArguments.Parse(["/src/repo"]);

        Assert.Equal("/src/repo", startup.RepositoryPath);
        Assert.Null(startup.Screen);
    }

    [Fact]
    public void Parse_AScreenThenAPath()
    {
        StartupArguments startup = StartupArguments.Parse(["--screen=1920,0,2560,1440", "/src/repo"]);

        Assert.Equal("/src/repo", startup.RepositoryPath);
        Assert.Equal(new ScreenArea(1920, 0, 2560, 1440), startup.Screen);
    }

    [Fact]
    public void Parse_AScreenLeftOfThePrimary_HasNegativeCoordinates()
        => Assert.Equal(new ScreenArea(-1920, -200, 1920, 1080), StartupArguments.Parse(["--screen=-1920,-200,1920,1080"]).Screen);

    [Theory]
    [InlineData(@"C:\src\my repo")]
    [InlineData("/src/a=b")]
    [InlineData("/src/--screen=1,2,3,4")]
    public void Parse_APathWithSpacesOrAnEqualsSign_IsOnePath(string path)
        => Assert.Equal(path, StartupArguments.Parse(["--screen=0,0,1920,1080", path]).RepositoryPath);

    [Theory]
    [InlineData("--screen=")]
    [InlineData("--screen=1,2,3")]
    [InlineData("--screen=1,2,3,4,5")]
    [InlineData("--screen=a,b,c,d")]
    [InlineData("--screen=1.5,0,1920,1080")]
    [InlineData("--screen=0,0,0,1080")]
    [InlineData("--screen=0,0,1920,-1")]
    public void Parse_AMalformedScreen_IsIgnored_AndThePathStillOpens(string option)
    {
        StartupArguments startup = StartupArguments.Parse([option, "/src/repo"]);

        Assert.Null(startup.Screen);
        Assert.Equal("/src/repo", startup.RepositoryPath);
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("-x")]
    [InlineData("--screen")]
    public void Parse_AnUnknownOption_IsIgnored(string option)
        => Assert.Equal("/src/repo", StartupArguments.Parse([option, "/src/repo"]).RepositoryPath);

    [Fact]
    public void Parse_DoubleDash_EndsTheOptions_SoAPathStartingWithADashOpens()
    {
        StartupArguments startup = StartupArguments.Parse(["--screen=0,0,1920,1080", "--", "-odd-folder"]);

        Assert.Equal("-odd-folder", startup.RepositoryPath);
        Assert.Equal(new ScreenArea(0, 0, 1920, 1080), startup.Screen);
    }

    [Fact]
    public void Parse_AfterDoubleDash_AScreenIsAPathNoMore()
        => Assert.Null(StartupArguments.Parse(["--", "--screen=0,0,1920,1080"]).Screen);

    [Fact]
    public void Parse_OnlyTheFirstPathOpens()
        => Assert.Equal("/src/one", StartupArguments.Parse(["/src/one", "/src/two"]).RepositoryPath);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("--screen=0,0,1920,1080")]
    public void Parse_NoPath_IsTheStartWindow(string argument)
        => Assert.Null(StartupArguments.Parse([argument]).RepositoryPath);

    [Fact]
    public void Parse_NoArgument_IsTheStartWindow()
        => Assert.Null(StartupArguments.Parse([]).RepositoryPath);

    [Fact]
    public void Parse_Nothing_IsNothing()
    {
        StartupArguments startup = StartupArguments.Parse(null);

        Assert.Null(startup.RepositoryPath);
        Assert.Null(startup.Screen);
    }

    [Fact]
    public void Parse_APathIsTrimmed_AsTheFirstArgumentAlwaysWas()
        => Assert.Equal("/src/repo", StartupArguments.Parse(["  /src/repo  "]).RepositoryPath);

    [Fact]
    public void FormatScreen_WritesTheOptionParseReads_InTheInvariantCulture()
    {
        ScreenArea screen = new(-1920, 0, 2560, 1440);
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            // A culture that groups digits with a non-breaking space and signs with a different minus.
            CultureInfo.CurrentCulture = new CultureInfo("fr-CH");

            string option = StartupArguments.FormatScreen(screen);

            Assert.Equal("--screen=-1920,0,2560,1440", option);
            Assert.Equal(screen, StartupArguments.Parse([option]).Screen);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
