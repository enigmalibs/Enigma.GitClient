using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Checking out a remote branch whose local branch already exists elsewhere: GitKraken's "reset local
/// to here", asked in a content dialog — against real git, with a bare <c>origin</c> a second clone
/// moves on.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ResetLocalToHereTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ResetLocalToHereTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the question

    [Fact]
    public void ARemoteAheadOfItsLocal_AsksToReset_WithTheResetAsTheDefault()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "main", "Theirs on main");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Close;
            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("origin/main", isRemote: true));

            ContentDialog question = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Reset \"main\" to \"origin/main\"?", question.Title);
            Assert.Equal("Reset local to here", question.PrimaryButtonText);
            Assert.Equal("Cancel", question.CloseButtonText);
            Assert.Equal(DefaultButton.Primary, question.DefaultButton);

            string content = Assert.IsType<string>(question.Content);
            Assert.Contains($"\"main\" is on {Short(Git(world.Local, "rev-parse", "main"))} here", content, StringComparison.Ordinal);
            Assert.Contains($"\"origin/main\" on {Short(Git(world.Local, "rev-parse", "origin/main"))}", content, StringComparison.Ordinal);
            Assert.Contains("Nothing is left behind", content, StringComparison.Ordinal);
            Assert.Contains("Uncommitted changes come along", content, StringComparison.Ordinal);

            // main is checked out already: checking it out "where it is" means nothing.
            Assert.True(string.IsNullOrEmpty(question.SecondaryButtonText));
        });
    }

    [Fact]
    public void ResetLocalToHere_MovesTheLocalBranchToTheRemotesCommit_AndChecksItOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "topic", "Theirs on topic");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Primary;
            Assert.True(await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true));

            Assert.Equal(Git(world.Local, "rev-parse", "origin/topic"), Git(world.Local, "rev-parse", "topic"));
            Assert.Equal("topic", Git(world.Local, "rev-parse", "--abbrev-ref", "HEAD"));
            Assert.Equal("origin/topic", Git(world.Local, "rev-parse", "--abbrev-ref", "topic@{upstream}"));

            RecordedNotification done = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("\"topic\" reset to \"origin/topic\"", done.Title);
            Assert.Equal(InfoBarSeverity.Success, done.Severity);
        });
    }

    [Fact]
    public void CheckOutAsItIs_ChecksTheLocalBranchOut_WithoutMovingIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            string before = Git(world.Local, "rev-parse", "topic");
            MoveOrigin(world, "topic", "Theirs on topic");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Secondary;
            Assert.True(await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true));

            Assert.Equal("Check out \"topic\"", services.Dialogs.Last!.SecondaryButtonText);
            Assert.Equal(before, Git(world.Local, "rev-parse", "topic"));
            Assert.Equal("topic", Git(world.Local, "rev-parse", "--abbrev-ref", "HEAD"));
        });
    }

    [Fact]
    public void Cancel_MovesNothing_AndChecksNothingOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            string before = Git(world.Local, "rev-parse", "topic");
            MoveOrigin(world, "topic", "Theirs on topic");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Close;
            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true));

            Assert.Equal(before, Git(world.Local, "rev-parse", "topic"));
            Assert.Equal("main", Git(world.Local, "rev-parse", "--abbrev-ref", "HEAD"));
            Assert.Empty(services.InfoBar.Shown);
        });
    }

    [Fact]
    public void ALocalWithCommitsOfItsOwn_NamesThem_AndCancelIsTheDefault()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "topic", "Theirs on topic");

            Git(world.Local, "checkout", "topic");
            Commit(world.Local, "src/mine.txt", "mine\n", "Mine, never pushed");
            Git(world.Local, "checkout", "main");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Close;
            await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true);

            ContentDialog question = Assert.Single(services.Dialogs.Shown);
            Assert.Equal(DefaultButton.Close, question.DefaultButton);

            string content = Assert.IsType<string>(question.Content);
            Assert.Contains("These commits are only on \"topic\", and are left behind:", content, StringComparison.Ordinal);
            Assert.Contains("Mine, never pushed", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Theirs on topic", content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheLeftBehindList_StopsAtTenCommits()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "topic", "Theirs on topic");

            Git(world.Local, "checkout", "topic");

            for (int index = 1; index <= 12; index++)
            {
                Commit(world.Local, $"src/mine{index}.txt", $"{index}\n", $"Mine {index}");
            }

            Git(world.Local, "checkout", "main");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Close;
            await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true);

            string content = Assert.IsType<string>(services.Dialogs.Last!.Content);
            Assert.Equal(BranchOperations.MaximumListedCommits, content.Split('\n').Count(line => line.StartsWith("Mine ", StringComparison.Ordinal)));
            Assert.Contains("…and more", content, StringComparison.Ordinal);
        });
    }

    // ---------------------------------------------------------------- no question to ask

    [Fact]
    public void ALevelLocalBranch_IsCheckedOutWithoutAsking()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            await OpenAsync(services, world);

            Assert.True(await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true));

            Assert.Empty(services.Dialogs.Shown);
            Assert.Equal("topic", Git(world.Local, "rev-parse", "--abbrev-ref", "HEAD"));
        });
    }

    [Fact]
    public void ALevelLocalBranchAlreadyCheckedOut_SaysSo_AndMovesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            await OpenAsync(services, world);

            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("origin/main", isRemote: true));

            Assert.Empty(services.Dialogs.Shown);
            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Already checked out", note.Title);
        });
    }

    [Fact]
    public void WithNoLocalBranch_TheTrackingBranchIsCreated_AsBefore()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "fresh", "Theirs on a fresh branch");
            await OpenAsync(services, world);

            Assert.True(await services.Get<IBranchOperations>().CheckoutAsync("origin/fresh", isRemote: true));

            Assert.Empty(services.Dialogs.Shown);
            Assert.Equal("fresh", Git(world.Local, "rev-parse", "--abbrev-ref", "HEAD"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Branch created");
        });
    }

    // ---------------------------------------------------------------- work is never lost

    [Fact]
    public void AFileTheResetWouldOverwrite_StopsIt_AndNothingMoves()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            string before = Git(world.Local, "rev-parse", "topic");
            MoveOrigin(world, "topic", "Theirs on topic", "src/theirs.txt");

            // Never committed, and the remote's commit brings a file of the same name.
            string mine = Path.Combine(world.Local.WorkTreePath, "src", "theirs.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
            File.WriteAllText(mine, "mine\n");
            await OpenAsync(services, world);

            services.Dialogs.Result = DialogResult.Primary;
            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true));

            Assert.Equal(before, Git(world.Local, "rev-parse", "topic"));
            Assert.Equal("main", Git(world.Local, "rev-parse", "--abbrev-ref", "HEAD"));
            Assert.Equal("mine\n", File.ReadAllText(mine));

            RecordedNotification failure = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not reset \"topic\"", failure.Title);
        });
    }

    // ---------------------------------------------------------------- every way in

    [Fact]
    public void TheHistorysRemoteBadge_AsksTheSameQuestion()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "topic", "Theirs on topic");
            await OpenAsync(services, world);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            HistoryBranchViewModel remote = history.Rows.SelectMany(row => row.Branches).Single(branch => branch.Name == "origin/topic");
            services.Dialogs.Result = DialogResult.Primary;

            await remote.Commands.Checkout.ExecuteAsync(remote);

            Assert.Equal("Reset \"topic\" to \"origin/topic\"?", Assert.Single(services.Dialogs.Shown).Title);
            Assert.Equal(Git(world.Local, "rev-parse", "origin/topic"), Git(world.Local, "rev-parse", "HEAD"));

            // Read again: topic and origin/topic are one badge now, on the checked-out line.
            HistoryBranchViewModel topic = history.Rows.SelectMany(row => row.Badges).OfType<HistoryBranchViewModel>().Single(branch => branch.Name == "topic");
            Assert.True(topic.IsCurrent);
            Assert.Equal("origin/topic", topic.Remote?.Name);
        });
    }

    [Fact]
    public void TheBranchesDialogsRemoteLine_AsksTheSameQuestion()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildAsync(services);
            MoveOrigin(world, "topic", "Theirs on topic");
            await OpenAsync(services, world);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel row = page.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.IsRemote && candidate.FullName == "origin/topic");
            services.Dialogs.Result = DialogResult.Close;

            await page.CheckoutCommand.ExecuteAsync(row);

            Assert.Equal("Reset \"topic\" to \"origin/topic\"?", Assert.Single(services.Dialogs.Shown).Title);
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// The repository under test, its bare remote, and a second clone that moves the remote on.
    /// </summary>
    private sealed record World(RepositoryHandle Local, string Origin, string Other);

    /// <summary>
    /// <c>main</c> and <c>topic</c>, both published and tracking <c>origin</c>, level with it; <c>main</c>
    /// checked out.
    /// </summary>
    private static async Task<World> BuildAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle local = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "local"), "main");
        Commit(local, "README.md", "# one\n", "Add the readme");

        string origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(origin);
        Git(local.WorkTreePath, "init", "--bare", origin);
        Git(origin, "symbolic-ref", "HEAD", "refs/heads/main");

        Git(local, "remote", "add", "origin", origin);
        Git(local, "push", "--set-upstream", "origin", "main");
        Git(local, "branch", "topic");
        Git(local, "push", "--set-upstream", "origin", "topic");

        string other = Path.Combine(root, "other");
        Git(root, "clone", origin, other);

        return new World(local, origin, other);
    }

    /// <summary>
    /// Moves a branch of the remote on by one commit from the second clone, and fetches it here.
    /// </summary>
    private static void MoveOrigin(World world, string branch, string subject, string path = "src/theirs.txt")
    {
        Git(world.Other, "fetch", "origin");
        Git(world.Other, "checkout", "-B", branch, "origin/main");

        string full = Path.Combine(world.Other, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, subject + "\n");

        Git(world.Other, "add", "--all");
        Git(world.Other, "commit", "-m", subject);
        Git(world.Other, "push", "--force", "origin", branch);

        Git(world.Local, "fetch", "origin");
    }

    private static async Task OpenAsync(TestServices services, World world)
        => await services.Get<IRepositoryContext>().OpenAsync(world.Local);

    private static string Short(string sha) => sha[..7];

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static string Git(RepositoryHandle repository, params string[] arguments)
        => Git(repository.WorkTreePath, arguments);

    private static string Git(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
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
