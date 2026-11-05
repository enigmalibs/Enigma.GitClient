using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The order a history line draws its badges in: the checked-out branch, the local branches, the
/// remote ones, anything else, and the tags last — each group by name (FEATURE-D7D5).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryBadgeOrderTests
{
    private const string Sha = "3fa9c07e1b2d4c5a6f7e8d9c0b1a2f3e4d5c6b7a";

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryBadgeOrderTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the comparer

    [Fact]
    public void Sort_PutsTheCurrentBranchFirst_ThenLocal_ThenRemote_ThenOthers_ThenTags()
    {
        RefBadgeItem[] shuffled =
        [
            new(GitRefKind.Tag, "v2", false),
            new(GitRefKind.RemoteBranch, "origin/zeta", false),
            new(GitRefKind.Stash, "stash@{0}", false),
            new(GitRefKind.LocalBranch, "topic", false),
            new(GitRefKind.Tag, "a-tag", false),
            new(GitRefKind.RemoteBranch, "origin/alpha", false),
            new(GitRefKind.LocalBranch, "main", true),
            new(GitRefKind.LocalBranch, "Feature", false),
            new(GitRefKind.Note, "notes/commits", false),
        ];

        List<RefBadgeItem> sorted = RefBadgeOrder.Sort(shuffled);

        Assert.Equal(
            ["main", "Feature", "topic", "origin/alpha", "origin/zeta", "notes/commits", "stash@{0}", "a-tag", "v2"],
            sorted.Select(badge => badge.Name));
    }

    [Fact]
    public void Sort_ComparesNamesWithoutRegardToCase()
    {
        List<RefBadgeItem> sorted = RefBadgeOrder.Sort(
        [
            new(GitRefKind.LocalBranch, "beta", false),
            new(GitRefKind.LocalBranch, "Alpha", false),
            new(GitRefKind.LocalBranch, "gamma", false),
        ]);

        Assert.Equal(["Alpha", "beta", "gamma"], sorted.Select(badge => badge.Name));
    }

    [Fact]
    public void ABranchDrawnWithItsUpstream_IsInTheLocalGroup()
    {
        Assert.Equal(1, RefBadgeOrder.Group(new RefBadgeItem(GitRefKind.LocalBranch, "topic", false, "origin/topic")));
        Assert.Equal(0, RefBadgeOrder.Group(new RefBadgeItem(GitRefKind.LocalBranch, "main", true, "origin/main")));
    }

    // ---------------------------------------------------------------- a line

    [Fact]
    public void ALineBuiltFromRefsInTheWrongOrder_DrawsThemInBadgeOrder()
    {
        GitSignature author = new("Ada Lovelace", "ada@example.com", DateTimeOffset.UnixEpoch);
        GitCommit commit = new(Sha, [], author, author, "Release", string.Empty);

        GitRef[] refs =
        [
            new GitTag("refs/tags/v1", Sha, null, null, string.Empty, DateTimeOffset.UnixEpoch),
            Branch("refs/remotes/origin/zeta"),
            Branch("refs/heads/topic"),
            Branch("refs/heads/main", isCurrent: true),
            new GitTag("refs/tags/a-tag", Sha, null, null, string.Empty, DateTimeOffset.UnixEpoch),
        ];

        CommitRowViewModel row = new(commit, new GraphRow(Sha, 0, 0, false, true, [], 0), refs, true, DateTimeOffset.UnixEpoch);

        Assert.Equal(["main", "topic", "origin/zeta", "a-tag", "v1"], row.Refs.Select(badge => badge.Name));
        Assert.Equal(["main", "topic", "origin/zeta", "a-tag", "v1"], row.Badges.Cast<RefBadgeItem>().Select(badge => badge.Name));
    }

    [Fact]
    public void AgainstRealGit_TheLinesBadgesAreInBadgeOrder()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            // On the one commit: main (current, with its upstream), topic, origin/zeta, two tags — made
            // in an order that is none of the expected one.
            Git(repository, "tag", "v1");
            Git(repository, "branch", "topic");
            Git(repository, "push", "origin", "HEAD:refs/heads/zeta");
            Git(repository, "tag", "a-tag");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            CommitRowViewModel row = page.Rows.Single(line => line.Subject == "Add the readme");

            Assert.Equal(["main", "topic", "origin/zeta", "a-tag", "v1"], row.Refs.Select(badge => badge.Name));
            Assert.Equal("origin/main", row.Refs[0].Upstream);
        });
    }

    // ---------------------------------------------------------------- helpers

    private static GitBranch Branch(string fullName, bool isCurrent = false)
        => new(fullName, Sha, isCurrent, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, "Release");

    /// <summary>
    /// A repository whose one commit is on <c>main</c>, published to a bare <c>origin</c> that
    /// <c>main</c> tracks.
    /// </summary>
    private static async Task<RepositoryHandle> BuildPublishedAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "local"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        string origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(origin);
        Git(repository, "init", "--bare", origin);
        Git(repository, "remote", "add", "origin", origin);
        Git(repository, "push", "--set-upstream", "origin", "main");
        Git(repository, "fetch", "origin");

        return repository;
    }

    private static string Git(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}
