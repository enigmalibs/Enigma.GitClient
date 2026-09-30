using Enigma.GitClient.Core.Diagnostics;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests;

public sealed class ProductInformationTests
{
    [Fact]
    public void GetVersion_ReturnsANonEmptyVersion()
        => Assert.False(string.IsNullOrWhiteSpace(ProductInformation.GetVersion()));

    [Fact]
    public void Version_IsWhatGetVersionSays_WithoutTheRevision()
    {
        Assert.Equal(ProductInformation.GetVersion(), ProductInformation.Version);
        Assert.DoesNotContain("+", ProductInformation.Version, System.StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayName_IsTheProductInWords()
        => Assert.Equal("Enigma Git Client", ProductInformation.DisplayName);

    [Fact]
    public void Copyright_IsTheOneTheBuildStamps()
        => Assert.Contains("Josué Clément", ProductInformation.Copyright, System.StringComparison.Ordinal);

    [Fact]
    public void ScopeStatement_MentionsTheRebaseExclusion()
        => Assert.Contains("never rebases", ProductInformation.ScopeStatement);
}
