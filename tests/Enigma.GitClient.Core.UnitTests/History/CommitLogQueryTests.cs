using System;
using Enigma.GitClient.Core.History;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.History;

public sealed class CommitLogQueryTests
{
    [Fact]
    public void Defaults_WalkEveryRefInDateOrder()
    {
        CommitLogQuery query = new();

        Assert.Equal(CommitLogScope.AllRefs, query.Scope);
        Assert.Equal(CommitLogOrdering.Date, query.Ordering);
        Assert.Equal(CommitLogQuery.DefaultPageSize, query.Take);
        Assert.Equal(0, query.Skip);
        Assert.False(query.FirstParentOnly);
        Assert.False(query.IsFiltered);
    }

    [Fact]
    public void NextPage_AdvancesSkipByTake()
    {
        CommitLogQuery query = new() { Take = 100 };

        Assert.Equal(100, query.NextPage().Skip);
        Assert.Equal(200, query.NextPage().NextPage().Skip);
        Assert.Equal(100, query.NextPage().Take);
    }

    [Theory]
    [InlineData("author")]
    [InlineData("message")]
    [InlineData("path")]
    [InlineData("since")]
    [InlineData("until")]
    [InlineData("first-parent")]
    public void IsFiltered_IsTrueForAnyFilter(string which)
    {
        CommitLogQuery query = which switch
        {
            "author" => new CommitLogQuery { AuthorFilter = "ada" },
            "message" => new CommitLogQuery { MessageFilter = "fix" },
            "path" => new CommitLogQuery { PathFilters = ["src/"] },
            "since" => new CommitLogQuery { Since = DateTimeOffset.UnixEpoch },
            "until" => new CommitLogQuery { Until = DateTimeOffset.UnixEpoch },
            _ => new CommitLogQuery { FirstParentOnly = true },
        };

        Assert.True(query.IsFiltered);
    }

    [Fact]
    public void IsFiltered_IgnoresABlankFilter()
        => Assert.False(new CommitLogQuery { AuthorFilter = "   ", MessageFilter = string.Empty }.IsFiltered);
}
