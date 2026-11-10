using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A window on a submodule: the mark that says whose it is, and the band while it is on no branch —
/// with the branch at HEAD to check out when there is one, a remote one otherwise, and always a new
/// branch. A window on anything else shows neither.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SubmoduleWindowTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string OtherSha = "fedcba9876543210fedcba9876543210fedcba98";

    private readonly HeadlessAvaloniaFixture _fixture;

    public SubmoduleWindowTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string Folder(params string[] segments)
        => Path.Combine([OperatingSystem.IsWindows() ? @"C:\src" : "/src", .. segments]);

    private static readonly RepositoryHandle Common = new(Folder("MainApp", "libs", "Common"), Folder("MainApp", ".git", "modules", "libs", "Common"));

    private static GitBranch Branch(string fullName, string sha)
        => new(fullName, sha, false, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, "A subject");

    private static (TestServices Services, FakeSubmoduleService Fake, RecordingBranchOperations Branches) Build(string? superproject)
    {
        FakeSubmoduleService fake = new() { Superproject = superproject };
        RecordingBranchOperations branches = new();

        TestServices services = TestServices.Build(configure: collection =>
        {
            collection.RemoveAll<ISubmoduleService>();
            collection.AddSingleton<ISubmoduleService>(fake);
            collection.RemoveAll<IBranchOperations>();
            collection.AddSingleton<IBranchOperations>(branches);
        });

        return (services, fake, branches);
    }

    private static async Task<SubmoduleWindowViewModel> OpenAsync(TestServices services, HeadState head, RefCollection refs)
    {
        FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();
        reader.Head = head;
        reader.Refs = refs;

        SubmoduleWindowViewModel window = services.Get<SubmoduleWindowViewModel>();
        await services.Get<IRepositoryContext>().OpenAsync(Common);
        await window.ReadSuperprojectAsync();

        return window;
    }

    private static HeadState Detached(string sha) => new(IsUnborn: false, IsDetached: true, null, sha, RepositoryOperation.None);

    private static HeadState OnBranch(string branch) => new(IsUnborn: false, IsDetached: false, branch, Sha, RepositoryOperation.None);

    [Fact]
    public void TheMark_SaysWhoseSubmoduleItIs()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, _) = Build(Folder("MainApp"));
            using (services)
            {
                SubmoduleWindowViewModel window = await OpenAsync(services, OnBranch("main"), RefCollection.Empty);

                Assert.True(window.IsSubmodule);
                Assert.Equal($"Submodule libs/Common of MainApp — {Folder("MainApp")}", window.MarkTip);
                Assert.False(window.IsDetachedBandVisible);
            }
        });
    }

    [Fact]
    public void OnNoBranch_TheBandOffersTheLocalBranchAtHead_AndANewOne()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, RecordingBranchOperations branches) = Build(Folder("MainApp"));
            using (services)
            {
                RefCollection refs = new(
                    [Branch("refs/heads/main", Sha), Branch("refs/heads/old", OtherSha)],
                    [Branch("refs/remotes/origin/main", Sha)],
                    [],
                    []);

                SubmoduleWindowViewModel window = await OpenAsync(services, Detached(Sha), refs);

                Assert.True(window.IsDetachedBandVisible);
                Assert.Equal("You're on no branch — commits made here are easy to lose.", window.DetachedMessage);
                Assert.True(window.OffersCheckout);
                Assert.Equal("Check out main", window.CheckoutBranchHeader);

                await window.CheckoutBranchCommand.ExecuteAsync(null);
                Assert.Equal([("main", false)], branches.CheckedOut);

                await window.CreateBranchCommand.ExecuteAsync(null);
                Assert.Equal([(Sha, "0123456")], branches.Created);
            }
        });
    }

    [Fact]
    public void WithOnlyARemoteBranchAtHead_TheBandOffersIt_AsATrackingCheckout()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, RecordingBranchOperations branches) = Build(Folder("MainApp"));
            using (services)
            {
                RefCollection refs = new(
                    [Branch("refs/heads/old", OtherSha)],
                    [Branch("refs/remotes/origin/HEAD", Sha), Branch("refs/remotes/origin/main", Sha)],
                    [],
                    []);

                SubmoduleWindowViewModel window = await OpenAsync(services, Detached(Sha), refs);

                Assert.Equal("Check out origin/main", window.CheckoutBranchHeader);

                await window.CheckoutBranchCommand.ExecuteAsync(null);
                Assert.Equal([("origin/main", true)], branches.CheckedOut);
            }
        });
    }

    [Fact]
    public void WithNoBranchAtHead_TheBandOffersOnlyANewOne()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, _) = Build(Folder("MainApp"));
            using (services)
            {
                SubmoduleWindowViewModel window = await OpenAsync(services, Detached(Sha), RefCollection.Empty);

                Assert.True(window.IsDetachedBandVisible);
                Assert.False(window.OffersCheckout);
                Assert.False(window.CheckoutBranchCommand.CanExecute(null));
                Assert.True(window.CreateBranchCommand.CanExecute(null));
            }
        });
    }

    [Fact]
    public void ARepositoryThatIsNotASubmodule_ShowsNeitherTheMarkNorTheBand()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, _) = Build(superproject: null);
            using (services)
            {
                SubmoduleWindowViewModel window = await OpenAsync(services, Detached(Sha), RefCollection.Empty);

                Assert.False(window.IsSubmodule);
                Assert.Equal(string.Empty, window.MarkTip);
                Assert.False(window.IsDetachedBandVisible);
            }
        });
    }

    [Fact]
    public void OnceOnABranch_TheBandGoes()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, _) = Build(Folder("MainApp"));
            using (services)
            {
                SubmoduleWindowViewModel window = await OpenAsync(services, Detached(Sha), RefCollection.Empty);
                Assert.True(window.IsDetachedBandVisible);

                ((FakeRefReader)services.Get<IRefReader>()).Head = OnBranch("main");
                await services.Get<IRepositoryContext>().RefreshAsync();

                Assert.False(window.IsDetachedBandVisible);
            }
        });
    }

    /// <summary>An <see cref="IBranchOperations"/> that records the checkouts and creations asked of it.</summary>
    private sealed class RecordingBranchOperations : IBranchOperations
    {
        public List<(string Name, bool IsRemote)> CheckedOut { get; } = [];

        public List<(string? StartPoint, string? Label)> Created { get; } = [];

        public Task<bool> CreateAsync(string? startPoint = null, string? startPointLabel = null)
        {
            Created.Add((startPoint, startPointLabel));
            return Task.FromResult(true);
        }

        public Task<bool> CheckoutAsync(string name, bool isRemote)
        {
            CheckedOut.Add((name, isRemote));
            return Task.FromResult(true);
        }

        public Task<bool> RenameAsync(string name) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string name, bool isRemote) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(IReadOnlyList<BranchToDelete> branches) => throw new NotSupportedException();

        public Task<bool> SetUpstreamAsync(string name, string? currentUpstream) => throw new NotSupportedException();
    }
}
