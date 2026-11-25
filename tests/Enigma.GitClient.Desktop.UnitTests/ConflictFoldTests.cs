using System.Linq;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Agreed text on the conflict page (CODE-REVIEW-6E56 PERF-02): a long stretch shows only the lines
/// next to the conflicts, folds the rest behind an expand row, and builds the folded lines only when
/// they are asked for.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ConflictFoldTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ConflictFoldTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static ConflictRegion Agreed(int count)
        => new([.. Enumerable.Range(1, count).Select(index => $"line {index}\n")]);

    [Fact]
    public void ALongStretchBetweenTwoConflicts_BuildsOnlyItsContextLines()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();

            ConflictRegionViewModel region = new(page, Agreed(5_000), 0, ourLine: 10, baseLine: 10, theirLine: 10, context: 3);

            Assert.True(region.IsFolded);
            Assert.Equal(4_994, region.FoldedLineCount);
            Assert.Equal("4994 unchanged lines", region.FoldedText);
            Assert.Equal(["line 1", "line 2", "line 3"], region.StableLines.Select(line => line.Text));
            Assert.Equal(["11", "12", "13"], region.StableLines.Select(line => line.Number));
            Assert.Equal(["line 4998", "line 4999", "line 5000"], region.StableTailLines.Select(line => line.Text));
            Assert.Equal(["5008", "5009", "5010"], region.StableTailLines.Select(line => line.Number));
        });
    }

    [Fact]
    public void Expanding_ShowsEveryLine_NumberedAsBefore()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();
            ConflictRegionViewModel region = new(page, Agreed(5_000), 0, ourLine: 10, context: 3);

            region.ExpandCommand.Execute(null);

            Assert.False(region.IsFolded);
            Assert.False(region.ExpandCommand.CanExecute(null));
            Assert.Empty(region.StableTailLines);
            Assert.Equal(5_000, region.StableLines.Count);
            Assert.Equal("11", region.StableLines[0].Number);
            Assert.Equal("5010", region.StableLines[^1].Number);
            Assert.Equal("line 5000", region.StableLines[^1].Text);
        });
    }

    [Theory]
    [InlineData(6, false, 0)]
    [InlineData(7, true, 1)]
    public void AStretchNoLongerThanTwiceTheContext_IsNotFolded(int count, bool folded, int hidden)
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();

            ConflictRegionViewModel region = new(page, Agreed(count), 0, context: 3);

            Assert.Equal(folded, region.IsFolded);
            Assert.Equal(hidden, region.FoldedLineCount);
            Assert.Equal(count - hidden, region.StableLines.Count + region.StableTailLines.Count);
        });
    }

    [Fact]
    public void ThePartsOfTheFileBeforeTheFirstConflictAndAfterTheLast_KeepOnlyTheSideNextToAConflict()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();

            ConflictRegionViewModel start = new(page, Agreed(100), 0, context: 3, conflictBefore: false, conflictAfter: true);
            ConflictRegionViewModel end = new(page, Agreed(100), 0, context: 3, conflictBefore: true, conflictAfter: false);

            Assert.Empty(start.StableLines);
            Assert.Equal(["line 98", "line 99", "line 100"], start.StableTailLines.Select(line => line.Text));
            Assert.Equal(97, start.FoldedLineCount);

            Assert.Equal(["line 1", "line 2", "line 3"], end.StableLines.Select(line => line.Text));
            Assert.Empty(end.StableTailLines);
            Assert.Equal(97, end.FoldedLineCount);
        });
    }

    [Fact]
    public void WithoutAContext_AgreedTextIsNeverFolded()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();

            ConflictRegionViewModel region = new(page, Agreed(500), 0);

            Assert.False(region.IsFolded);
            Assert.Equal(500, region.StableLines.Count);
        });
    }

    [Fact]
    public void AConflictedRegion_HasNothingToFold()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();
            string[] ours = [.. Enumerable.Range(1, 50).Select(index => $"ours {index}\n")];

            ConflictRegionViewModel region = new(page, new ConflictRegion([], ours, ["theirs\n"]), 1, context: 3);

            Assert.False(region.IsFolded);
            Assert.Empty(region.StableLines);
            Assert.Equal(50, region.OurLines.Count);
        });
    }
}
