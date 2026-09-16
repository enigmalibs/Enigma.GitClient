using Enigma.GitClient.Core.Diagnostics;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests;

public sealed class ProductInformationTests
{
    [Fact]
    public void GetVersion_ReturnsANonEmptyVersion()
        => Assert.False(string.IsNullOrWhiteSpace(ProductInformation.GetVersion()));

    [Fact]
    public void ScopeStatement_MentionsTheRebaseExclusion()
        => Assert.Contains("never rebases", ProductInformation.ScopeStatement);
}
