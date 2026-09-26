using Enigma.GitClient.Core.Diagnostics;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests;

public sealed class ProductVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.0.0+9a1b2c3d4e5f60718293a4b5c6d7e8f901234567", "1.0.0", "9a1b2c3")]
    [InlineData("1.1.0-rc.1+abc", "1.1.0-rc.1", "abc")]
    [InlineData("  2.0.0+  ", "2.0.0", null)]
    [InlineData("1.0.0+", "1.0.0", null)]
    public void From_SplitsTheVersionFromTheRevision(string informational, string version, string? sha)
    {
        ProductVersion product = ProductVersion.From(informational, "9.9.9");

        Assert.Equal(version, product.Version);
        Assert.Equal(sha, product.BuildSha);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+9a1b2c3")]
    public void From_FallsBackWhenTheInformationalVersionSaysNothing(string? informational)
    {
        ProductVersion product = ProductVersion.From(informational, "1.2.3");

        Assert.Equal("1.2.3", product.Version);
        Assert.Null(product.BuildSha);
    }

    [Fact]
    public void From_AlwaysHasAVersionToShow()
        => Assert.Equal(ProductVersion.Unknown, ProductVersion.From(null, null).Version);
}
