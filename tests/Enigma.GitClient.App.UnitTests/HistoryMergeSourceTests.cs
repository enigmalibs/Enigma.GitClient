using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Merging from the history: a branch set as the merge source, then merged into another, from its
/// badge or from the line's menu — against a real repository.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryMergeSourceTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryMergeSourceTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// <c>main</c> (checked out) one commit behind <c>feature</c>, with <c>twin</c> on the same
    /// commit as <c>feature</c> — a line carrying two branches.
    /// </summary>
    private static async Task<(TestServices Services, RepositoryHandle Repository, HistoryPageViewModel History)> OpenAsync()
    {
        TestServices services = TestServices.Build(useRealRefReader: true);

        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "merges"), "main");

        Commit(repository, "README.md", "# one\n", "Add the readme");
        Git(repository, "checkout", "-b", "feature");
        Commit(repository, "src/feature.txt", "from the feature\n", "Work on the feature");
        Git(repository, "branch", "twin");
        Git(repository, "checkout", "main");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.OnAppearingAsync();
        await history.ReloadAsync();

        return (services, repository, history);
    }

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        start.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        start.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        start.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }

    private static CommitRowViewModel Line(HistoryPageViewModel history, string subject)
        => history.Rows.Single(row => row.Subject == subject);

    private static HistoryBranchViewModel Branch(HistoryPageViewModel history, string name)
        => history.Rows.SelectMany(row => row.Branches).Single(branch => branch.Name == name);

    private static string[] Headers(CommitRowViewModel row)
        => [.. row.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

    [Fact]
    public void ALineWithTwoBranches_OffersEachOfThemAsTheMergeSource()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            CommitRowViewModel line = Line(history, "Work on the feature");

            Assert.Equal(["feature", "twin"], line.Branches.Select(branch => branch.Name).Order());
            Assert.Contains("Set \"feature\" as merge source", Headers(line));
            Assert.Contains("Set \"twin\" as merge source", Headers(line));

            // No source yet, so nothing to merge anywhere.
            Assert.DoesNotContain(Headers(line), header => header.StartsWith("Merge ", StringComparison.Ordinal));

            // The commit's own actions are still the line's.
            Assert.Contains("Show what it changed", Headers(line));
            Assert.Contains("Check out this commit (detaches HEAD)", Headers(line));
            Assert.Contains("Create branch here…", Headers(line));
        });
    }

    [Fact]
    public void SettingASource_OffersMergingItIntoEveryOtherLocalBranch()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel feature = Branch(history, "feature");
            feature.Commands.SetAsMergeSource.Execute(feature);

            Assert.True(history.HasMergeSource);
            Assert.Equal("Merge source: feature", history.MergeSourceSummary);
            Assert.True(feature.IsMergeSource);
            Assert.False(feature.CanSetAsMergeSource);

            // The source's own line: set the other branch, or merge into it — never into itself.
            string[] featureLine = Headers(Line(history, "Work on the feature"));
            Assert.DoesNotContain("Set \"feature\" as merge source", featureLine);
            Assert.Contains("Merge \"feature\" into \"twin\"", featureLine);
            Assert.DoesNotContain("Merge \"feature\" into \"feature\"", featureLine);

            // main's line.
            Assert.Contains("Merge \"feature\" into \"main\"", Headers(Line(history, "Add the readme")));

            HistoryBranchViewModel main = Branch(history, "main");
            Assert.True(main.CanMergeInto);
            Assert.Equal("Merge \"feature\" into \"main\"", main.MergeIntoHeader);
            Assert.False(feature.CanMergeInto);
        });
    }

    [Fact]
    public void MergingTheSourceIntoABranch_MergesItAndKeepsTheSource()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel feature = Branch(history, "feature");
            feature.Commands.SetAsMergeSource.Execute(feature);

            HistoryBranchViewModel main = Branch(history, "main");
            await main.Commands.MergeInto.ExecuteAsync(main);

            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src", "feature.txt")));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);

            // One source, several merges: it stays until it is cleared.
            Assert.Equal(new MergeSource("feature", false), history.MergeSource);
        });
    }

    [Fact]
    public void MergingIntoABranchThatIsNotCheckedOut_ChecksItOutFirst()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel main = Branch(history, "main");
            main.Commands.SetAsMergeSource.Execute(main);

            HistoryBranchViewModel twin = Branch(history, "twin");
            await twin.Commands.MergeInto.ExecuteAsync(twin);

            Assert.Equal("twin", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void ClearingTheSource_TakesTheMergesAway()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel feature = Branch(history, "feature");
            feature.Commands.SetAsMergeSource.Execute(feature);
            Assert.True(history.ClearMergeSourceCommand.CanExecute(null));

            history.ClearMergeSourceCommand.Execute(null);

            Assert.False(history.HasMergeSource);
            Assert.False(Branch(history, "main").CanMergeInto);
            Assert.DoesNotContain(Headers(Line(history, "Add the readme")), header => header.StartsWith("Merge ", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheSource_GoesWhenItsBranchDoes()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel twin = Branch(history, "twin");
            twin.Commands.SetAsMergeSource.Execute(twin);

            Git(repository, "branch", "-D", "twin");
            await services.Get<IRepositoryContext>().RefreshAsync();

            Assert.Null(history.MergeSource);
        });
    }

    [Fact]
    public void TheSource_StaysWhileItsBranchIsThere()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel twin = Branch(history, "twin");
            twin.Commands.SetAsMergeSource.Execute(twin);

            await services.Get<IRepositoryContext>().RefreshAsync();

            Assert.Equal("twin", history.MergeSource?.Name);
        });
    }

    [Fact]
    public void TheSource_IsForgottenWithTheRepository()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel twin = Branch(history, "twin");
            twin.Commands.SetAsMergeSource.Execute(twin);

            services.Get<IRepositoryContext>().Close();

            Assert.Null(history.MergeSource);
        });
    }

    [Fact]
    public void ARemoteBranch_CanBeTheSourceButNeverTheDestination()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel remote = new(new RefBadgeItem(GitRefKind.RemoteBranch, "origin/feature", false), history.BranchCommands);
            HistoryBranchViewModel main = Branch(history, "main");

            remote.Commands.SetAsMergeSource.Execute(remote);
            Assert.Equal(new MergeSource("origin/feature", true), history.MergeSource);
            Assert.True(main.CanMergeInto);
            Assert.Equal(new BranchDropRequest("origin/feature", true, "main", false, true), main.MergeRequest);

            main.Commands.SetAsMergeSource.Execute(main);
            Assert.False(remote.CanMergeInto);
            Assert.False(remote.Commands.MergeInto.CanExecute(remote));
        });
    }

    [Fact]
    public void TheCheckedOutBranch_CannotBeCheckedOutDeletedOrMergedIntoItself()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            HistoryBranchViewModel main = Branch(history, "main");

            Assert.True(main.IsCurrent);
            Assert.False(main.Commands.Checkout.CanExecute(main));
            Assert.False(main.Commands.Delete.CanExecute(main));
            Assert.False(main.CanMergeIntoCurrent);

            HistoryBranchViewModel feature = Branch(history, "feature");
            Assert.True(feature.CanMergeIntoCurrent);
            Assert.Equal("Merge \"feature\" into \"main\"", feature.MergeIntoCurrentHeader);
        });
    }

    [Fact]
    public void Badges_AreBranchesWithAMenuAndEverythingElseWithout()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            Git(repository, "tag", "v1.0.0", "feature");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await history.ReloadAsync();

            CommitRowViewModel line = Line(history, "Work on the feature");

            Assert.Equal(3, line.Badges.Count);
            Assert.Equal(2, line.Badges.OfType<HistoryBranchViewModel>().Count());
            Assert.Equal("v1.0.0", Assert.Single(line.Badges.OfType<RefBadgeItem>()).Name);
        });
    }
}
