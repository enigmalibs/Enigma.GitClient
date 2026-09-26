using System;
using System.Linq;
using Enigma.GitClient.Core.Git;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

public sealed class GitCommandFactoryTests
{
    private readonly GitCommandFactory _factory = new();

    [Fact]
    public void Create_PrependsTheStandardGlobalOptions()
    {
        GitCommand command = _factory.Create("/repo", "status", "--porcelain=v2");

        Assert.Equal(
            ["--no-pager", "-c", "core.quotePath=false", "-c", "color.ui=false", "status", "--porcelain=v2"],
            command.Arguments);
    }

    [Fact]
    public void Create_ResolvesTheVerbPastTheGlobalOptions()
        => Assert.Equal("status", _factory.Create("/repo", "status").Verb);

    [Fact]
    public void Create_PassesValuesThroughVerbatimSoNoShellQuotingIsNeeded()
    {
        const string awkward = "a path with spaces, a 'quote' and $(not a substitution)";

        GitCommand command = _factory.Create("/repo", "add", "--", awkward);

        Assert.Equal(awkward, command.Arguments[^1]);
    }

    [Fact]
    public void Create_RejectsAnEmptyArgumentList()
        => Assert.Throws<ArgumentException>(() => _factory.Create("/repo"));

    [Fact]
    public void Create_RejectsAForbiddenOperation()
        => Assert.Throws<NotSupportedException>(() => _factory.Create("/repo", "rebase", "main"));

    [Fact]
    public void CreateWithInput_CarriesTheStandardInput()
    {
        GitCommand command = _factory.CreateWithInput("/repo", "a message", ["commit", "-F", "-"]);

        Assert.Equal("a message", command.StandardInput);
    }

    [Fact]
    public void ToString_RedactsCredentials()
    {
        GitCommand command = _factory.Create("/repo", "clone", "https://user:secret@example.com/r.git");

        Assert.DoesNotContain("secret", command.ToString(), StringComparison.Ordinal);
        Assert.Contains("***", command.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalOptions_DisableTheFeaturesThatWouldBreakParsing()
    {
        Assert.Contains("--no-pager", GitCommandFactory.GlobalOptions);
        Assert.Contains("core.quotePath=false", GitCommandFactory.GlobalOptions);
        Assert.Contains("color.ui=false", GitCommandFactory.GlobalOptions);
    }

    [Fact]
    public void Create_CopiesTheArgumentsSoLaterMutationCannotAffectTheCommand()
    {
        string[] arguments = ["status"];
        GitCommand command = _factory.Create("/repo", arguments);

        arguments[0] = "rebase";

        Assert.Equal("status", command.Verb);
        Assert.Equal("status", command.Arguments.Last());
    }
}
