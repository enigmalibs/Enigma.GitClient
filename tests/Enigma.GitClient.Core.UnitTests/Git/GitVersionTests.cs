using Enigma.GitClient.Core.Git;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

public sealed class GitVersionTests
{
    [Theory]
    [InlineData("git version 2.45.1", 2, 45, 1)]
    [InlineData("git version 2.45.1.windows.1", 2, 45, 1)]
    [InlineData("git version 2.39.5 (Apple Git-154)", 2, 39, 5)]
    [InlineData("  git version 2.20.0\n", 2, 20, 0)]
    [InlineData("2.51.3", 2, 51, 3)]
    [InlineData("git version 3.0", 3, 0, 0)]
    public void TryParse_ReadsTheNumericComponents(string output, int major, int minor, int patch)
    {
        Assert.True(GitVersion.TryParse(output, out GitVersion version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("git version")]
    [InlineData("not a version at all")]
    public void TryParse_RejectsUnparseableOutput(string? output)
        => Assert.False(GitVersion.TryParse(output, out _));

    [Fact]
    public void TryParse_KeepsTheVendorSuffixInRaw()
    {
        Assert.True(GitVersion.TryParse("git version 2.45.1.windows.1", out GitVersion version));
        Assert.Equal("2.45.1.windows.1", version.Raw);
    }

    [Theory]
    [InlineData("2.19.9", true)]
    [InlineData("2.20.0", false)]
    [InlineData("2.20.1", false)]
    [InlineData("3.0.0", false)]
    [InlineData("1.99.99", true)]
    public void Comparison_AgainstTheSupportedMinimum(string candidate, bool isTooOld)
    {
        Assert.True(GitVersion.TryParse(candidate, out GitVersion version));
        Assert.Equal(isTooOld, version < GitVersion.Minimum);
    }

    [Fact]
    public void ToString_RendersTheThreeNumericComponents()
    {
        Assert.True(GitVersion.TryParse("git version 2.45.1.windows.1", out GitVersion version));
        Assert.Equal("2.45.1", version.ToString());
    }
}
