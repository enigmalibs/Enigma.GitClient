using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Sync;

/// <summary>
/// Covers the two things that turn git's terminal chatter into something a window can show: the
/// progress parser and the failure classifier.
/// </summary>
public sealed class SyncParsingTests
{
    // ---------------------------------------------------------------- progress

    [Theory]
    [InlineData("Enumerating objects: 27, done.", SyncStage.Enumerating)]
    [InlineData("Counting objects: 100% (27/27), done.", SyncStage.Counting)]
    [InlineData("Compressing objects:  62% (13/21)", SyncStage.Compressing)]
    [InlineData("Receiving objects:  73% (1234/1690), 4.02 MiB | 2.01 MiB/s", SyncStage.Receiving)]
    [InlineData("Writing objects: 100% (21/21), 2.31 KiB | 2.31 MiB/s, done.", SyncStage.Writing)]
    [InlineData("Resolving deltas:  40% (4/10)", SyncStage.Resolving)]
    [InlineData("Updating files:  85% (17/20)", SyncStage.CheckingOut)]
    public void Parse_RecognisesEveryStageGitReports(string line, SyncStage expected)
        => Assert.Equal(expected, SyncProgressParser.Parse(line)!.Stage);

    [Fact]
    public void Parse_ReadsThePercentageAndTheCounts()
    {
        SyncProgress progress = SyncProgressParser.Parse("Receiving objects:  73% (1234/1690), 4.02 MiB | 2.01 MiB/s")!;

        Assert.Equal(73, progress.Percent);
        Assert.Equal(1234, progress.Current);
        Assert.Equal(1690, progress.Total);
        Assert.True(progress.IsDeterminate);
        Assert.Equal("Receiving objects", progress.Description);
    }

    [Fact]
    public void Parse_HandlesAStageWithNoPercentageYet()
    {
        SyncProgress progress = SyncProgressParser.Parse("Enumerating objects: 27, done.")!;

        Assert.Equal(SyncStage.Enumerating, progress.Stage);
        Assert.Null(progress.Percent);
        Assert.False(progress.IsDeterminate);
    }

    [Fact]
    public void Parse_KeepsWhatItDoesNotUnderstandRatherThanDroppingIt()
    {
        SyncProgress progress = SyncProgressParser.Parse("remote: Something the server wanted to say")!;

        // git says useful things that are not progress; swallowing them leaves a user watching a bar
        // that never explains itself.
        Assert.Equal(SyncStage.Other, progress.Stage);
        Assert.Equal("remote: Something the server wanted to say", progress.Text);
        Assert.Equal("remote: Something the server wanted to say", progress.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_SaysNothingAboutAnEmptyChunk(string? chunk)
        => Assert.Null(SyncProgressParser.Parse(chunk));

    [Fact]
    public void Parse_ReadsAWholeTranscriptInOrder()
    {
        // Captured from a real fetch. git rewrites one line with carriage returns, so each chunk is
        // a state of that line rather than a new message.
        string[] transcript =
        [
            "remote: Enumerating objects: 27, done.",
            "remote: Counting objects:  11% (3/27)",
            "remote: Counting objects: 100% (27/27), done.",
            "remote: Compressing objects:  50% (10/20)",
            "remote: Compressing objects: 100% (20/20), done.",
            "Receiving objects:  22% (6/27)",
            "Receiving objects: 100% (27/27), 5.20 KiB | 5.20 MiB/s, done.",
            "Resolving deltas: 100% (8/8), done.",
        ];

        List<SyncProgress> parsed = [];

        foreach (string line in transcript)
        {
            if (SyncProgressParser.Parse(line) is { } progress)
            {
                parsed.Add(progress);
            }
        }

        Assert.Equal(8, parsed.Count);

        // "remote:" prefixes the server's own copy of the stage line, which is not the stage name.
        Assert.Equal(SyncStage.Other, parsed[0].Stage);
        Assert.Equal(SyncStage.Receiving, parsed[5].Stage);
        Assert.Equal(100, parsed[^1].Percent);
    }

    [Fact]
    public void ToStage_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => SyncProgressParser.ToStage(null!));

    // ---------------------------------------------------------------- failures

    [Fact]
    public void Map_RecognisesAnAuthenticationFailure()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "remote: Support for password authentication was removed on August 13, 2021.\n"
            + "fatal: Authentication failed for 'https://example.com/repo.git/'");

        Assert.Equal(SyncFailureKind.Authentication, failure.Kind);
        Assert.Contains("credential helper", failure.Message, StringComparison.Ordinal);
        Assert.False(failure.IsRecoverableLocally);
    }

    [Fact]
    public void Map_RecognisesAMissingCredentialRatherThanHangingOnTheInvisiblePrompt()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "fatal: could not read Username for 'https://example.com': terminal prompts disabled");

        Assert.Equal(SyncFailureKind.Authentication, failure.Kind);
    }

    [Fact]
    public void Map_RecognisesAHostKeyProblem()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "@@@ WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED! @@@\nHost key verification failed.");

        Assert.Equal(SyncFailureKind.HostKey, failure.Kind);
        Assert.Contains("known_hosts", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RecognisesARejectedPush()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            " ! [rejected]        main -> main (non-fast-forward)\n"
            + "error: failed to push some refs to 'origin'\n"
            + "hint: Updates were rejected because the tip of your current branch is behind");

        Assert.Equal(SyncFailureKind.NonFastForward, failure.Kind);
        Assert.Contains("Pull first", failure.Message, StringComparison.Ordinal);
        Assert.True(failure.IsRecoverableLocally);
    }

    [Fact]
    public void Map_TellsAStaleLeaseApartFromAPlainRejection()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            " ! [rejected]        main -> main (stale info)\nerror: failed to push some refs to 'origin'");

        // They are the same refusal to a user who reads only "rejected", and completely different
        // things to do about it.
        Assert.Equal(SyncFailureKind.StaleLease, failure.Kind);
        Assert.Contains("Fetch, look at what arrived", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RecognisesAMissingUpstream()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "There is no tracking information for the current branch.\n"
            + "Please specify which branch you want to merge with.");

        Assert.Equal(SyncFailureKind.NoUpstream, failure.Kind);
        Assert.True(failure.IsRecoverableLocally);
    }

    [Fact]
    public void Map_RecognisesAnUnreachableHost()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "fatal: unable to access 'https://example.com/repo.git/': Could not resolve host: example.com");

        Assert.Equal(SyncFailureKind.Network, failure.Kind);
        Assert.Contains("could not be reached", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RecognisesAMergeThatStoppedOnConflicts()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "CONFLICT (content): Merge conflict in src/app.txt\nAutomatic merge failed; fix conflicts and then commit the result.");

        Assert.Equal(SyncFailureKind.MergeConflict, failure.Kind);
        Assert.True(failure.IsRecoverableLocally);
    }

    [Fact]
    public void Map_RecognisesUncommittedWorkInTheWay()
    {
        SyncFailure failure = SyncErrorMapper.Map(
            "error: Your local changes to the following files would be overwritten by merge:\n\tsrc/app.txt\n"
            + "Please commit your changes or stash them before you merge.");

        Assert.Equal(SyncFailureKind.LocalChanges, failure.Kind);
        Assert.Contains("stash", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_PassesAnythingElseThroughVerbatim()
    {
        SyncFailure failure = SyncErrorMapper.Map("fatal: something entirely new went wrong");

        // A wrong explanation is worse than git's own.
        Assert.Equal(SyncFailureKind.Unknown, failure.Kind);
        Assert.Equal("fatal: something entirely new went wrong", failure.Message);
    }

    [Fact]
    public void FirstMeaningfulLine_PrefersADiagnosticOverAProgressRemnant()
    {
        string line = SyncErrorMapper.FirstMeaningfulLine(
            "Receiving objects: 100% (27/27), done.\nfatal: the thing that actually went wrong");

        Assert.Equal("fatal: the thing that actually went wrong", line);
    }

    [Fact]
    public void FirstMeaningfulLine_FallsBackToTheFirstLineThereIs()
        => Assert.Equal("something", SyncErrorMapper.FirstMeaningfulLine("\n\n  something  \nmore"));

    [Fact]
    public void FirstMeaningfulLine_SaysSoWhenGitSaidNothing()
        => Assert.Equal("git reported no reason.", SyncErrorMapper.FirstMeaningfulLine("   \n  "));

    // ---------------------------------------------------------------- the argument vectors

    [Fact]
    public void BuildPullArguments_AlwaysRefusesToRebase()
    {
        foreach (PullStrategy strategy in Enum.GetValues<PullStrategy>())
        {
            List<string> arguments = SyncService.BuildPullArguments("origin", "main", strategy);

            // A repository configured with pull.rebase=true would otherwise rebase behind the user's
            // back — and this client does not rebase, ever.
            Assert.Contains("--no-rebase", arguments);
            Assert.DoesNotContain("--rebase", arguments);
            Assert.DoesNotContain(arguments, argument => argument.Contains("rebase", StringComparison.Ordinal)
                && !argument.StartsWith("--no-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void BuildPullArguments_AddsFastForwardOnlyWhenAsked()
    {
        Assert.DoesNotContain("--ff-only", SyncService.BuildPullArguments(null, null, PullStrategy.Merge));
        Assert.Contains("--ff-only", SyncService.BuildPullArguments(null, null, PullStrategy.FastForwardOnly));
    }

    [Fact]
    public void BuildPullArguments_LeavesTheUpstreamToGitWhenNoRemoteIsNamed()
    {
        List<string> arguments = SyncService.BuildPullArguments(null, null, PullStrategy.Merge);

        Assert.Equal(["pull", "--progress", "--no-rebase"], arguments);
    }

    [Fact]
    public void BuildFetchArguments_FetchesEverythingWhenNoRemoteIsNamed()
    {
        List<string> arguments = SyncService.BuildFetchArguments(null, prune: true, fetchTags: true);

        Assert.Contains("--all", arguments);
        Assert.Contains("--prune", arguments);
        Assert.Contains("--tags", arguments);
        Assert.Contains("--progress", arguments);
    }

    [Fact]
    public void BuildFetchArguments_NamesTheRemoteWhenThereIsOne()
    {
        List<string> arguments = SyncService.BuildFetchArguments("origin", prune: false, fetchTags: false);

        Assert.Contains("origin", arguments);
        Assert.DoesNotContain("--all", arguments);
        Assert.DoesNotContain("--prune", arguments);
        Assert.Contains("--no-tags", arguments);
    }

    [Fact]
    public void BuildPushArguments_NeverPassesABareForce()
    {
        List<string> arguments = SyncService.BuildPushArguments(new PushRequest
        {
            Remote = "origin",
            Branch = "main",
            ForceWithLease = true,
        });

        // The lease is the whole point: it refuses to clobber work that arrived since the last fetch.
        Assert.Contains("--force-with-lease", arguments);
        Assert.DoesNotContain("--force", arguments);
        Assert.DoesNotContain("-f", arguments);
    }

    [Fact]
    public void BuildPushArguments_SetsUpstreamAndFollowsTagsWhenAsked()
    {
        List<string> arguments = SyncService.BuildPushArguments(new PushRequest
        {
            Remote = "origin",
            Branch = "topic",
            SetUpstream = true,
            PushTags = true,
        });

        Assert.Contains("--set-upstream", arguments);
        Assert.Contains("--follow-tags", arguments);
        Assert.Equal("topic", arguments[^1]);
    }

    [Fact]
    public void BuildPushArguments_DeletesWithoutForcingOrTracking()
    {
        List<string> arguments = SyncService.BuildPushArguments(new PushRequest
        {
            Remote = "origin",
            Branch = "topic",
            Delete = true,
            SetUpstream = true,
            ForceWithLease = true,
            PushTags = true,
        });

        // A delete has nothing to track, nothing to lease and no tags to follow; passing them would
        // be git refusing the command for reasons the user cannot see.
        Assert.Contains("--delete", arguments);
        Assert.DoesNotContain("--set-upstream", arguments);
        Assert.DoesNotContain("--force-with-lease", arguments);
        Assert.DoesNotContain("--follow-tags", arguments);
    }

    [Fact]
    public void BuildPushArguments_DefaultsToOrigin()
        => Assert.Contains("origin", SyncService.BuildPushArguments(new PushRequest()));

    [Fact]
    public void BuildFastForwardArguments_NamesBothSidesInFullAndNeverForces()
    {
        List<string> arguments = SyncService.BuildFastForwardArguments("origin", "feature/login", "login");

        Assert.Equal(["fetch", "--progress", "--no-tags", "origin", "refs/heads/feature/login:refs/heads/login"], arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith('+'));
        Assert.DoesNotContain("--force", arguments);
    }

    [Theory]
    [InlineData("", "main", "main")]
    [InlineData("origin", " ", "main")]
    [InlineData("origin", "main", "")]
    [InlineData("--upload-pack=evil", "main", "main")]
    public void BuildFastForwardArguments_RefusesWhatCannotBeARefOrARemote(string remote, string remoteBranch, string localBranch)
        => Assert.ThrowsAny<ArgumentException>(() => SyncService.BuildFastForwardArguments(remote, remoteBranch, localBranch));
}
