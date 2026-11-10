using System.Collections.Generic;
using Enigma.GitClient.Core.Merging;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Merging;

/// <summary>
/// A submodule's conflict as the index holds it — the stages of <c>git ls-files -u</c> — and what the
/// conflict list says of it.
/// </summary>
public sealed class PinConflictParsingTests
{
    private const string A = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string B = "f00ba12345678901234567890123456789abcdef";
    private const string C = "c0ffee1234567890123456789012345678901234";

    [Fact]
    public void ParseUnmergedStages_ReadsEachPathsStagesWithTheirModes()
    {
        string output =
            $"160000 {A} 1\tlibs/Common\0" +
            $"160000 {B} 2\tlibs/Common\0" +
            $"160000 {C} 3\tlibs/Common\0" +
            $"100644 {A} 2\tREADME.md\0" +
            $"100644 {B} 3\tREADME.md\0";

        Dictionary<string, Dictionary<int, (string Mode, string Sha)>> stages = ConflictService.ParseUnmergedStages(output);

        Assert.Equal(("160000", B), stages["libs/Common"][2]);
        Assert.Equal(("160000", C), stages["libs/Common"][3]);
        Assert.Equal(3, stages["libs/Common"].Count);
        Assert.Equal(("100644", A), stages["README.md"][2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage\0")]
    [InlineData("160000 a1b2 0\tlibs/Common\0")]
    public void ParseUnmergedStages_ReadsNothingFromWhatIsNotAnUnmergedEntry(string? output)
        => Assert.Empty(ConflictService.ParseUnmergedStages(output));

    [Fact]
    public void APinConflict_HasBothCommits_AndNoTextToMerge()
    {
        ConflictFile pin = new("libs/Common", ConflictKind.BothModified, "UU", false) { IsSubmodule = true, OursSha = B, TheirsSha = C };

        Assert.True(pin.IsPinConflict);
        Assert.False(pin.HasThreeWayContent);
        Assert.Equal("Both sides moved this submodule to different commits.", pin.Description);
    }

    [Fact]
    public void ASubmoduleRemovedOnOneSide_IsLeftForATerminal()
    {
        ConflictFile removed = new("libs/Common", ConflictKind.DeletedByThem, "UD", false) { IsSubmodule = true, OursSha = B };

        Assert.False(removed.IsPinConflict);
        Assert.Contains("resolve this one in a terminal", removed.Description, System.StringComparison.Ordinal);
    }
}
