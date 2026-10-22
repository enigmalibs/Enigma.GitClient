using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A history line's age, measured again after the line was drawn, so "how long ago" stays true while
/// the automatic refresh redraws nothing (FEATURE-3D77).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryAgeTests
{
    private const string Sha = "3fa9c07e1b2d4c5a6f7e8d9c0b1a2f3e4d5c6b7a";

    private static readonly DateTimeOffset Committed = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryAgeTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static GraphRow Segment() => new(Sha, 0, 0, false, true, [], 0);

    /// <summary>A line drawn the moment its commit was made.</summary>
    private static CommitRowViewModel Row(bool absoluteDates = false)
    {
        GitSignature author = new("Ada Lovelace", "ada@example.com", Committed);
        GitCommit commit = new(Sha, [], author, author, "Fix the token refresh", string.Empty);

        return new CommitRowViewModel(commit, Segment(), null, false, Committed, absoluteDates: absoluteDates);
    }

    private static List<string?> Notifications(CommitRowViewModel row)
    {
        List<string?> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        return changed;
    }

    [Fact]
    public void ALineMeasuredLater_ShowsTheLaterAge()
    {
        _fixture.Run(() =>
        {
            CommitRowViewModel row = Row();
            Assert.Equal("just now", row.DateText);
            List<string?> changed = Notifications(row);

            row.RefreshRelativeDate(Committed.AddMinutes(5));

            Assert.Equal("5 minutes ago", row.RelativeDate);
            Assert.Equal("5 minutes ago", row.DateText);
            Assert.Contains(nameof(CommitRowViewModel.RelativeDate), changed);
            Assert.Contains(nameof(CommitRowViewModel.DateText), changed);
        });
    }

    [Fact]
    public void InAbsoluteMode_TheTooltipCarriesTheNewAge()
    {
        _fixture.Run(() =>
        {
            CommitRowViewModel row = Row(absoluteDates: true);
            string shown = row.DateText;
            List<string?> changed = Notifications(row);

            row.RefreshRelativeDate(Committed.AddHours(2));

            Assert.Equal(shown, row.DateText);
            Assert.Equal(row.AbsoluteDate, row.DateText);
            Assert.Equal("2 hours ago", row.DateTooltip);
            Assert.Contains(nameof(CommitRowViewModel.DateTooltip), changed);
        });
    }

    [Fact]
    public void TheSameAge_NotifiesNothing()
    {
        _fixture.Run(() =>
        {
            CommitRowViewModel row = Row();
            List<string?> changed = Notifications(row);

            row.RefreshRelativeDate(Committed.AddSeconds(10));

            Assert.Equal("just now", row.RelativeDate);
            Assert.Empty(changed);
        });
    }

    [Fact]
    public void TheUncommittedLine_HasNoAge()
    {
        _fixture.Run(() =>
        {
            CommitRowViewModel row = CommitRowViewModel.Uncommitted(Segment());
            List<string?> changed = Notifications(row);

            row.RefreshRelativeDate(Committed.AddDays(3));

            Assert.Equal(string.Empty, row.RelativeDate);
            Assert.Equal(string.Empty, row.DateText);
            Assert.Empty(changed);
        });
    }
}
