using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A push refused for unpushed submodule commits: the offer and its words, the submodule pushed before
/// the repository, and a submodule whose profile may not push to its remote refusing the whole thing —
/// against fakes of the transfer, the locator and the push guard.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SubmodulePushOfferTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly HeadlessAvaloniaFixture _fixture;

    public SubmodulePushOfferTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string Folder(params string[] segments)
        => Path.Combine([OperatingSystem.IsWindows() ? @"C:\src" : "/src", .. segments]);

    private static readonly RepositoryHandle Super = new(Folder("super"), Folder("super", ".git"));

    private static readonly RepositoryHandle Common = new(Folder("super", "libs", "Common"), Folder("super", ".git", "modules", "libs", "Common"));

    private static (TestServices Services, RecordingSyncService Sync, PushGuardDouble Guard) Build()
    {
        RecordingSyncService sync = new();
        PushGuardDouble guard = new();

        TestServices services = TestServices.Build(configure: collection =>
        {
            collection.RemoveAll<ISyncService>();
            collection.AddSingleton<ISyncService>(sync);
            collection.RemoveAll<IPushGuard>();
            collection.AddSingleton<IPushGuard>(guard);
            collection.RemoveAll<IRepositoryLocator>();
            collection.AddSingleton<IRepositoryLocator>(new LocatorDouble());
        });

        FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();
        reader.Head = new HeadState(IsUnborn: false, IsDetached: false, "main", Sha, RepositoryOperation.None);
        reader.Refs = new RefCollection(
            [new GitBranch("refs/heads/main", Sha, true, "origin/main", BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, "A subject")],
            [],
            [],
            []);

        return (services, sync, guard);
    }

    [Fact]
    public void TheOffer_NamesEachSubmoduleWithItsBranchAndRemote()
    {
        (string title, string message, string confirm) = SyncOperations.DescribeSubmodulePush(
            [new SyncOperations.SubmodulePush("libs/Common", Common, "main", "origin", SetUpstream: false)]);

        Assert.Equal("Push libs/Common first?", title);
        Assert.Contains("libs/Common: main → origin", message, StringComparison.Ordinal);
        Assert.Equal("Push libs/Common, then this repository", confirm);

        (string many, _, string manyConfirm) = SyncOperations.DescribeSubmodulePush(
        [
            new SyncOperations.SubmodulePush("libs/Common", Common, "main", "origin", SetUpstream: false),
            new SyncOperations.SubmodulePush("libs/Json", Common, "work", "origin", SetUpstream: true),
        ]);

        Assert.Equal("Push the submodules first?", many);
        Assert.Equal("Push the submodules, then this repository", manyConfirm);
    }

    [Fact]
    public void Accepted_TheSubmoduleIsPushedFirst_ThenTheRepository()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RecordingSyncService sync, _) = Build();
            using (services)
            {
                await services.Get<IRepositoryContext>().OpenAsync(Super);
                services.Dialogs.Script(DialogResult.Primary);

                bool pushed = await services.Get<ISyncOperations>().PushAsync();

                Assert.True(pushed);
                Assert.Equal(
                    [(Super.WorkTreePath, "main"), (Common.WorkTreePath, "main"), (Super.WorkTreePath, "main")],
                    sync.Pushes);
            }
        });
    }

    [Fact]
    public void Declined_NothingMorePushes()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RecordingSyncService sync, _) = Build();
            using (services)
            {
                await services.Get<IRepositoryContext>().OpenAsync(Super);
                services.Dialogs.Script(DialogResult.None);

                Assert.False(await services.Get<ISyncOperations>().PushAsync());
                Assert.Single(sync.Pushes);
            }
        });
    }

    [Fact]
    public void ASubmoduleItsProfileMayNotPushFrom_RefusesTheWholeThing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RecordingSyncService sync, PushGuardDouble guard) = Build();
            using (services)
            {
                await services.Get<IRepositoryContext>().OpenAsync(Super);
                services.Dialogs.Script(DialogResult.Primary);
                guard.RefuseFor = Common.WorkTreePath;

                Assert.False(await services.Get<ISyncOperations>().PushAsync());
                Assert.Single(sync.Pushes);
                Assert.Contains("libs/Common", services.InfoBar.Shown[^1].Message, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>Records the pushes, and refuses the repository's first one for an unpushed submodule.</summary>
    private sealed class RecordingSyncService : ISyncService
    {
        public List<(string Repository, string? Branch)> Pushes { get; } = [];

        public Task PushAsync(RepositoryHandle repository, PushRequest request, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Pushes.Add((repository.WorkTreePath, request.Branch));

            if (Pushes.Count == 1)
            {
                throw new SyncException(new SyncFailure(SyncFailureKind.UnpushedSubmodule, "Push it first.", string.Empty)
                {
                    SubmodulePaths = ["libs/Common"],
                });
            }

            return Task.CompletedTask;
        }

        public Task FetchAsync(RepositoryHandle repository, string? remote = null, bool prune = true, bool fetchTags = true, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PullAsync(RepositoryHandle repository, string? remote = null, string? branch = null, PullStrategy strategy = PullStrategy.Merge, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task FastForwardBranchAsync(RepositoryHandle repository, string remote, string remoteBranch, string localBranch, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PushTagAsync(RepositoryHandle repository, string remote, string tag, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>Allows every push, but the one from <see cref="RefuseFor"/>.</summary>
    private sealed class PushGuardDouble : IPushGuard
    {
        public string? RefuseFor { get; set; }

        public Task<PushPermission> CheckAsync(RepositoryHandle repository, string remote, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(repository.WorkTreePath, RefuseFor, StringComparison.Ordinal)
                ? PushPermission.Unchecked()
                : PushPermission.WithoutProfile);
    }

    /// <summary>Finds the repository and its one submodule without touching a disk.</summary>
    private sealed class LocatorDouble : IRepositoryLocator
    {
        public Task<RepositoryDiscoveryResult> DiscoverAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(RepositoryDiscoveryResult.Found(
                string.Equals(Path.GetFullPath(path), Common.WorkTreePath, StringComparison.OrdinalIgnoreCase) ? Common : Super));
    }
}
