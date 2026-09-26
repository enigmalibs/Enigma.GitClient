using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Git;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

public sealed class GitArgumentVectorTests
{
    [Fact]
    public void FindVerb_ReturnsTheFirstNonOptionArgument()
        => Assert.Equal("log", GitArgumentVector.FindVerb(["log", "--oneline"]));

    [Fact]
    public void FindVerb_SkipsGlobalOptionsThatTakeASeparateValue()
        => Assert.Equal(
            "status",
            GitArgumentVector.FindVerb(["-C", "/repo", "-c", "core.quotePath=false", "status", "--porcelain=v2"]));

    [Fact]
    public void FindVerb_SkipsStandaloneGlobalFlags()
        => Assert.Equal("diff", GitArgumentVector.FindVerb(["--no-pager", "--literal-pathspecs", "diff"]));

    [Fact]
    public void FindVerb_SkipsInlineValuedGlobalOptions()
        => Assert.Equal("show", GitArgumentVector.FindVerb(["--git-dir=/repo/.git", "show", "HEAD"]));

    [Fact]
    public void FindVerb_DoesNotMistakeAnOptionValueForTheVerb()
        => Assert.Equal("status", GitArgumentVector.FindVerb(["-C", "rebase", "status"]));

    [Fact]
    public void FindVerb_ReturnsNullWhenThereIsOnlyOptions()
        => Assert.Null(GitArgumentVector.FindVerb(["--no-pager", "-c", "color.ui=false"]));

    [Fact]
    public void EnumerateConfigOverrides_ReturnsEveryDashCValue()
    {
        List<string> overrides =
            [.. GitArgumentVector.EnumerateConfigOverrides(
                ["-c", "core.quotePath=false", "-c", "color.ui=false", "log"])];

        Assert.Equal(["core.quotePath=false", "color.ui=false"], overrides);
    }

    [Fact]
    public void EnumerateConfigOverrides_IgnoresATrailingDashCWithNoValue()
        => Assert.Empty(GitArgumentVector.EnumerateConfigOverrides(["log", "-c"]).ToList());
}
