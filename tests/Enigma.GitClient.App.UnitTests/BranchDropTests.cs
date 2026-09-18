using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives one branch being dropped onto another, against a real repository: the answers the flow
/// gives are the answers git gave it, and what moved is read back off HEAD.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BranchDropTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public BranchDropTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Builds <c>main</c> with a <c>feature</c> branch one commit ahead of it — the shape a
    /// fast-forward is possible on.
    /// </summary>
    private static async Task<RepositoryHandle> BuildAheadAsync(TestServices services)
    {
        RepositoryHandle repository = await InitAsync(services);

        await GitAsync(repository, "checkout", "-b", "feature");
        Write(repository, "src/feature.txt", "from the feature\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Work on the feature");

        await GitAsync(repository, "checkout", "main");

        return repository;
    }

    /// <summary>
    /// Builds <c>main</c> and <c>feature</c> with a commit each since they parted — the shape only
    /// a real merge can join.
    /// </summary>
    private static async Task<RepositoryHandle> BuildDivergedAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildAheadAsync(services);

        Write(repository, "src/main.txt", "from main\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Work on main");

        return repository;
    }

    private static async Task<RepositoryHandle> InitAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "drops"), "main");

        Write(repository, "README.md", "# one\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Add the readme");

        return repository;
    }

    private static void Write(RepositoryHandle repository, string relativePath, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
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
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static string Head(TestServices services)
        => services.Get<IRepositoryContext>().Head?.BranchName ?? string.Empty;

    // ---------------------------------------------------------------- the policy

    [Theory]
    // source, sourceIsRemote, target, targetIsRemote, expected
    [InlineData("feature", false, "main", false, true)]
    [InlineData("origin/feature", true, "main", false, true)]
    [InlineData("main", false, "main", false, false)]
    [InlineData("feature", false, "origin/main", true, false)]
    [InlineData("", false, "main", false, false)]
    [InlineData("feature", false, "", false, false)]
    public void CanDrop_AcceptsOnlyWhatCanBeMerged(
        string source,
        bool sourceIsRemote,
        string target,
        bool targetIsRemote,
        bool expected)
        => Assert.Equal(
            expected,
            BranchDropOperations.CanDrop(new BranchDropRequest(source, sourceIsRemote, target, targetIsRemote, false)));

    // ---------------------------------------------------------------- the flow

    [Fact]
    public void Drop_MergesIntoTheBranchAlreadyCheckedOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildDivergedAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            bool changed = await services.Get<IBranchDropOperations>()
                .DropAsync(new BranchDropRequest("feature", false, "main", false, TargetIsCurrent: true));

            Assert.True(changed);
            Assert.Equal("main", Head(services));

            // The merge is recorded on main and the feature's file is now there.
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/feature.txt")));

            // Nothing was asked: the menu that opened on the drop is the question, and asking again
            // here would be asking twice.
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void Drop_ChecksTheTargetOutFirstWhenItIsNotTheCurrentBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildDivergedAsync(services);

            // Standing on the source, dropping it onto the other branch.
            await GitAsync(repository, "checkout", "feature");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            Assert.Equal("feature", Head(services));

            bool changed = await services.Get<IBranchDropOperations>()
                .DropAsync(new BranchDropRequest("feature", false, "main", false, TargetIsCurrent: false));

            Assert.True(changed);

            // HEAD moved onto the target, and the merge landed there rather than on the source.
            Assert.Equal("main", Head(services));
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/main.txt")));
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/feature.txt")));
        });
    }

    [Fact]
    public void Drop_FastForwardOnlyMovesABranchThatIsSimplyBehind()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAheadAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            bool changed = await services.Get<IBranchDropOperations>()
                .DropAsync(
                    new BranchDropRequest("feature", false, "main", false, TargetIsCurrent: true),
                    FastForwardMode.Only);

            Assert.True(changed);
            Assert.Equal("main", Head(services));
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src/feature.txt")));
        });
    }

    [Fact]
    public void Drop_FastForwardOnlyRefusesADivergedBranchAndSaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildDivergedAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            bool changed = await services.Get<IBranchDropOperations>()
                .DropAsync(
                    new BranchDropRequest("feature", false, "main", false, TargetIsCurrent: true),
                    FastForwardMode.Only);

            Assert.False(changed);

            // Nothing was merged, and the reader was told why.
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "src/feature.txt")));
            Assert.NotNull(services.InfoBar.Last);
            Assert.Contains("feature", services.InfoBar.Last!.Title, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Drop_OnARemoteBranchIsRefusedBeforeAnythingIsAsked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildDivergedAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            bool changed = await services.Get<IBranchDropOperations>()
                .DropAsync(new BranchDropRequest("feature", false, "origin/main", true, TargetIsCurrent: false));

            Assert.False(changed);
            Assert.Equal("main", Head(services));

            Assert.NotNull(services.InfoBar.Last);
            Assert.Contains("remote", services.InfoBar.Last!.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Drop_OnItselfIsRefusedBeforeAnythingIsAsked()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildDivergedAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            bool changed = await services.Get<IBranchDropOperations>()
                .DropAsync(new BranchDropRequest("main", false, "main", false, TargetIsCurrent: true));

            Assert.False(changed);

            Assert.NotNull(services.InfoBar.Last);
            Assert.Contains("itself", services.InfoBar.Last!.Message, StringComparison.OrdinalIgnoreCase);
        });
    }
}
