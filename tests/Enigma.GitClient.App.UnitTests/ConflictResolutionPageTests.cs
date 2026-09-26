using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the conflict resolution page against real conflicting merges: the regions it builds, the
/// choices it offers, the preview it shows, and the two ways a merge ends.
/// </summary>
/// <remarks>
/// The conflicts come from a real repository rather than from a faked service. What is worth
/// pinning is that the page shows what git actually left behind — a fake would only pin that the
/// page can render a document a test wrote for it.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class ConflictResolutionPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ConflictResolutionPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- fixtures

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "conflicts"), "main");

        Write(repository, "src/app.txt", "one\ntwo\nthree\n");
        await CommitAllAsync(repository, "Add the file");

        return repository;
    }

    /// <summary>
    /// Leaves the repository mid-merge with one conflicted text file.
    /// </summary>
    private static async Task ConflictAsync(RepositoryHandle repository, TestServices services)
    {
        await GitAsync(repository, "checkout", "-b", "theirs");
        Write(repository, "src/app.txt", "one\ntheir version\nthree\n");
        await CommitAllAsync(repository, "Their version");

        await GitAsync(repository, "checkout", "main");
        Write(repository, "src/app.txt", "one\nour version\nthree\n");
        await CommitAllAsync(repository, "Our version");

        await services.Get<IRepositoryContext>().OpenAsync(repository);
        await services.Get<IMergeOperations>().MergeAsync("theirs");
        await services.Get<IRepositoryContext>().RefreshAsync();
    }

    /// <summary>
    /// Leaves the repository mid-merge with two conflicts in one file.
    /// </summary>
    private static async Task ConflictTwiceAsync(RepositoryHandle repository, TestServices services)
    {
        Write(repository, "src/app.txt", "one\ntwo\nthree\nfour\nfive\n");
        await CommitAllAsync(repository, "A longer file");

        await GitAsync(repository, "checkout", "-b", "theirs");
        Write(repository, "src/app.txt", "one\ntheir two\nthree\ntheir four\nfive\n");
        await CommitAllAsync(repository, "Their version");

        await GitAsync(repository, "checkout", "main");
        Write(repository, "src/app.txt", "one\nour two\nthree\nour four\nfive\n");
        await CommitAllAsync(repository, "Our version");

        await services.Get<IRepositoryContext>().OpenAsync(repository);
        await services.Get<IMergeOperations>().MergeAsync("theirs");
        await services.Get<IRepositoryContext>().RefreshAsync();
    }

    /// <summary>
    /// Opens the page and waits for the selected file to have been read.
    /// </summary>
    /// <remarks>
    /// Selecting a file starts reading its three stages; the page does not block on it, because a
    /// list whose selection waits on git is a list that stutters. A test has to wait for the read
    /// the way the view does — by watching the property it puts the result in.
    /// </remarks>
    private static async Task<ConflictResolutionPageViewModel> OpenAsync(TestServices services)
    {
        ConflictResolutionPageViewModel page = services.Get<ConflictResolutionPageViewModel>();
        await page.OnAppearingAsync();

        await WaitUntilAsync(() => !page.IsLoadingFile);

        return page;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    private static void Write(RepositoryHandle repository, string relativePath, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Read(RepositoryHandle repository, string relativePath)
        => File.ReadAllText(Path.Combine(repository.WorkTreePath, relativePath));

    private static async Task CommitAllAsync(RepositoryHandle repository, string message)
    {
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", message);
    }

    private static Task<string> GitAsync(RepositoryHandle repository, params string[] arguments)
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
            ? Task.FromResult(output)
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static ConflictRegionViewModel FirstConflict(ConflictResolutionPageViewModel page)
        => page.Regions.First(region => region.IsConflicted);

    // ---------------------------------------------------------------- the file list

    [Fact]
    public void Page_ListsTheFilesTheMergeStoppedOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            ConflictFileRowViewModel row = Assert.Single(page.Files);

            Assert.Equal("src/app.txt", row.Path);
            Assert.Equal("app.txt", row.Name);
            Assert.Equal("src", row.Directory);
            Assert.Equal("Both changed", row.StateLabel);
            Assert.False(row.IsResolved);

            // The first file still needing work is selected, so the page opens on something to do.
            Assert.Same(row, page.SelectedFile);
            Assert.Equal("0 of 1 file resolved", page.Progress);
        });
    }

    [Fact]
    public void Page_IsEmptyWhenThereIsNoMerge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.Empty(page.Files);
            Assert.False(page.HasConflicts);
            Assert.False(page.IsMergeInProgress);
            Assert.False(page.CanCommitMerge);
        });
    }

    // ---------------------------------------------------------------- the regions

    [Fact]
    public void Page_BuildsTheRegionsOfTheSelectedFile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.True(page.HasDocument);
            Assert.False(page.IsWholeFileChoice);

            ConflictRegionViewModel region = FirstConflict(page);

            Assert.Equal("Conflict 1", region.Title);
            Assert.Equal(["our version"], region.OurLines.Select(line => line.Text));
            Assert.Equal(["two"], region.BaseLines.Select(line => line.Text));
            Assert.Equal(["their version"], region.TheirLines.Select(line => line.Text));
            Assert.True(region.HasBase);
            Assert.Equal("Undecided", region.ChoiceLabel);

            // The agreed text around it is there too, or the panes would show a conflict with no
            // context at all.
            Assert.Contains(page.Regions, row => row.IsStable && row.StableLines.Any(line => line.Text == "one"));
            Assert.Equal("0 of 1 conflict resolved", page.RegionProgress);
        });
    }

    [Fact]
    public void TakeOurs_KeepsOurVersionAndSaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);
            ConflictRegionViewModel region = FirstConflict(page);

            page.TakeOursCommand.Execute(region);

            Assert.Equal(ConflictResolution.Ours, region.Resolution);
            Assert.True(region.IsOurs);
            Assert.True(region.IsResolved);
            Assert.Equal("Keeping ours", region.ChoiceLabel);
            Assert.Equal("one\nour version\nthree\n", page.Preview);
            Assert.Equal("1 of 1 conflict resolved", page.RegionProgress);
        });
    }

    [Fact]
    public void TakeTheirs_KeepsTheirVersion()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            page.TakeTheirsCommand.Execute(FirstConflict(page));

            Assert.Equal("one\ntheir version\nthree\n", page.Preview);
        });
    }

    [Fact]
    public void TakeBoth_KeepsBothInTheOrderThatWasAskedFor()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);
            ConflictRegionViewModel region = FirstConflict(page);

            page.TakeBothOursFirstCommand.Execute(region);
            Assert.Equal("one\nour version\ntheir version\nthree\n", page.Preview);

            page.TakeBothTheirsFirstCommand.Execute(region);
            Assert.Equal("one\ntheir version\nour version\nthree\n", page.Preview);
        });
    }

    [Fact]
    public void TakeBase_KeepsWhatBothSidesStartedFrom()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            page.TakeBaseCommand.Execute(FirstConflict(page));

            Assert.Equal("one\ntwo\nthree\n", page.Preview);
        });
    }

    [Fact]
    public void AnUnresolvedRegionIsPreviewedWithGitsOwnMarkers()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            // A preview that silently dropped what is undecided would say the file is finished.
            Assert.Contains("<<<<<<<", page.Preview, StringComparison.Ordinal);
            Assert.Contains(">>>>>>>", page.Preview, StringComparison.Ordinal);
            Assert.False(page.CanSave);
        });
    }

    [Fact]
    public void Editing_KeepsWhatWasWrittenAndCancellingKeepsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);
            ConflictRegionViewModel region = FirstConflict(page);

            page.EditRegionCommand.Execute(region);

            Assert.True(region.IsEditing);

            // The box opens on our side rather than on nothing, so an edit starts from something.
            Assert.Equal("our version\n", region.EditText);

            page.CancelEditCommand.Execute(region);

            Assert.False(region.IsEditing);
            Assert.Equal(ConflictResolution.Unresolved, region.Resolution);

            page.EditRegionCommand.Execute(region);
            region.EditText = "a version of my own\n";
            page.ApplyEditCommand.Execute(region);

            Assert.False(region.IsEditing);
            Assert.Equal(ConflictResolution.Custom, region.Resolution);
            Assert.Equal("Edited by hand", region.ChoiceLabel);
            Assert.Equal("one\na version of my own\nthree\n", page.Preview);
        });
    }

    [Fact]
    public void TakeAll_ResolvesEveryRegionOfTheFileAtOnce()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictTwiceAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.Equal(2, page.Regions.Count(region => region.IsConflicted));
            Assert.Equal("0 of 2 conflicts resolved", page.RegionProgress);

            page.TakeAllOursCommand.Execute(null);

            Assert.All(page.Regions.Where(region => region.IsConflicted), region => Assert.True(region.IsOurs));
            Assert.Equal("one\nour two\nthree\nour four\nfive\n", page.Preview);
            Assert.Equal("2 of 2 conflicts resolved", page.RegionProgress);
            Assert.True(page.CanSave);

            page.TakeAllTheirsCommand.Execute(null);

            Assert.Equal("one\ntheir two\nthree\ntheir four\nfive\n", page.Preview);

            page.ClearChoicesCommand.Execute(null);

            Assert.All(page.Regions.Where(region => region.IsConflicted), region => Assert.True(region.NeedsDecision));
            Assert.False(page.CanSave);
        });
    }

    [Fact]
    public void NextUnresolved_WalksTheRegionsAndComesBackRound()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictTwiceAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            List<ConflictRegionViewModel> conflicts = [.. page.Regions.Where(region => region.IsConflicted)];

            Assert.Same(conflicts[0], page.SelectedRegion);

            page.NextUnresolvedCommand.Execute(null);
            Assert.Same(conflicts[1], page.SelectedRegion);

            // Past the last one it starts again, so the shortcut always lands somewhere.
            page.NextUnresolvedCommand.Execute(null);
            Assert.Same(conflicts[0], page.SelectedRegion);
        });
    }

    [Fact]
    public void TheKeyboardActsOnTheRegionTheReaderIsOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictTwiceAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            List<ConflictRegionViewModel> conflicts = [.. page.Regions.Where(region => region.IsConflicted)];

            page.TakeOursForSelectedCommand.Execute(null);
            Assert.True(conflicts[0].IsOurs);

            page.NextUnresolvedCommand.Execute(null);
            page.TakeTheirsForSelectedCommand.Execute(null);

            Assert.True(conflicts[1].IsTheirs);
            Assert.Equal("one\nour two\nthree\ntheir four\nfive\n", page.Preview);
        });
    }

    // ---------------------------------------------------------------- saving

    [Fact]
    public void Save_WritesExactlyThePreviewAndMarksTheFileResolved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            page.TakeBothOursFirstCommand.Execute(FirstConflict(page));

            string preview = page.Preview;

            await page.SaveCommand.ExecuteAsync(null);

            Assert.Equal(preview, Read(repository, "src/app.txt"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "File resolved");

            // The row stays in the list so the count still means something.
            ConflictFileRowViewModel row = Assert.Single(page.Files);

            Assert.True(row.IsResolved);
            Assert.Equal("Resolved", row.StateLabel);
            Assert.Equal("1 of 1 file resolved", page.Progress);
            Assert.False(page.HasUnresolved);
        });
    }

    [Fact]
    public void Save_IsRefusedUntilEveryRegionHasBeenDecided()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictTwiceAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.False(page.SaveCommand.CanExecute(null));

            page.TakeOursCommand.Execute(page.Regions.First(region => region.IsConflicted));

            Assert.False(page.SaveCommand.CanExecute(null));

            page.TakeTheirsCommand.Execute(page.Regions.Last(region => region.IsConflicted));

            Assert.True(page.SaveCommand.CanExecute(null));
        });
    }

    // ---------------------------------------------------------------- whole-file choices

    [Fact]
    public void ABinaryConflictIsOfferedAsAChoiceBetweenTwoVersions()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            byte[] ours = [0x00, 0x01, 0x02];
            byte[] theirs = [0x00, 0x09, 0x08];

            await GitAsync(repository, "checkout", "-b", "theirs");
            File.WriteAllBytes(Path.Combine(repository.WorkTreePath, "blob.bin"), theirs);
            await CommitAllAsync(repository, "Their blob");

            await GitAsync(repository, "checkout", "main");
            File.WriteAllBytes(Path.Combine(repository.WorkTreePath, "blob.bin"), ours);
            await CommitAllAsync(repository, "Our blob");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await services.Get<IMergeOperations>().MergeAsync("theirs");
            await services.Get<IRepositoryContext>().RefreshAsync();

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.False(page.HasDocument);
            Assert.True(page.IsWholeFileChoice);
            Assert.Contains("no text to merge", page.WholeFileMessage, StringComparison.Ordinal);

            await page.KeepOursCommand.ExecuteAsync(null);

            Assert.Equal(ours, File.ReadAllBytes(Path.Combine(repository.WorkTreePath, "blob.bin")));
            Assert.True(Assert.Single(page.Files).IsResolved);
        });
    }

    [Fact]
    public void ADeletionAgainstAChangeSaysWhatKeepingEachSideMeans()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            await GitAsync(repository, "checkout", "-b", "theirs");
            File.Delete(Path.Combine(repository.WorkTreePath, "src", "app.txt"));
            await CommitAllAsync(repository, "They deleted it");

            await GitAsync(repository, "checkout", "main");
            Write(repository, "src/app.txt", "one\nchanged by us\nthree\n");
            await CommitAllAsync(repository, "We changed it");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await services.Get<IMergeOperations>().MergeAsync("theirs");
            await services.Get<IRepositoryContext>().RefreshAsync();

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.True(page.IsWholeFileChoice);
            Assert.Equal("Keep ours", page.KeepOursDescription);
            Assert.Equal("Keep theirs (delete the file)", page.KeepTheirsDescription);

            await page.KeepTheirsCommand.ExecuteAsync(null);

            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
        });
    }

    // ---------------------------------------------------------------- bulk, commit and abort

    [Fact]
    public void KeepingOneSideEverywhereAsksFirstAndThenResolvesEveryFile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "src/other.txt", "start\n");
            await CommitAllAsync(repository, "A second file");

            await GitAsync(repository, "checkout", "-b", "theirs");
            Write(repository, "src/app.txt", "one\ntheir version\nthree\n");
            Write(repository, "src/other.txt", "theirs\n");
            await CommitAllAsync(repository, "Their version");

            await GitAsync(repository, "checkout", "main");
            Write(repository, "src/app.txt", "one\nour version\nthree\n");
            Write(repository, "src/other.txt", "ours\n");
            await CommitAllAsync(repository, "Our version");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await services.Get<IMergeOperations>().MergeAsync("theirs");
            await services.Get<IRepositoryContext>().RefreshAsync();

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.Equal(2, page.Files.Count);

            // Refused first: nothing is resolved by a bulk action the user did not confirm.
            services.Dialogs.Script(DialogResult.Close, DialogResult.Primary);

            await page.TakeEveryFileOursCommand.ExecuteAsync(null);

            Assert.True(page.HasUnresolved);

            await page.TakeEveryFileOursCommand.ExecuteAsync(null);

            Assert.False(page.HasUnresolved);
            Assert.Equal("2 of 2 files resolved", page.Progress);
            Assert.Equal("one\nour version\nthree\n", Read(repository, "src/app.txt"));
            Assert.Equal("ours\n", Read(repository, "src/other.txt"));
        });
    }

    [Fact]
    public void CommittingTheMergeWaitsUntilEveryFileIsResolved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            Assert.True(page.IsMergeInProgress);
            Assert.False(page.CanCommitMerge);
            Assert.False(page.CommitMergeCommand.CanExecute(null));

            page.TakeOursCommand.Execute(FirstConflict(page));
            await page.SaveCommand.ExecuteAsync(null);

            Assert.True(page.CommitMergeCommand.CanExecute(null));

            await page.CommitMergeCommand.ExecuteAsync(null);

            // The merge is recorded, and the page has nothing left to show.
            string parents = await GitAsync(repository, "rev-list", "--parents", "-1", "HEAD");

            Assert.Equal(3, parents.Trim().Split(' ').Length);
            Assert.False(page.IsMergeInProgress);
            Assert.Empty(page.Files);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Merge committed");
        });
    }

    [Fact]
    public void AbandoningTheMergeAsksFirst()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            services.Dialogs.Script(DialogResult.Close, DialogResult.Primary);

            await page.AbortMergeCommand.ExecuteAsync(null);

            Assert.True(page.IsMergeInProgress);
            Assert.Equal("Abandon the merge", services.Dialogs.Last!.Title);

            await page.AbortMergeCommand.ExecuteAsync(null);

            Assert.False(page.IsMergeInProgress);
            Assert.Empty(page.Files);
            Assert.Equal("one\nour version\nthree\n", Read(repository, "src/app.txt"));
        });
    }

    // ---------------------------------------------------------------- the shell

    [Fact]
    public void TheRailCarriesTheConflictsPageOnlyWhileAMergeIsInProgress()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            IShellNavigation navigation = services.Get<IShellNavigation>();

            await ConflictAsync(repository, services);

            Assert.True(shell.IsMergeInProgress);
            Assert.Contains(navigation.Service.Items, item => item.Header == "Conflicts");
            Assert.True(shell.ResolveConflictsCommand.CanExecute(null));

            shell.ResolveConflictsCommand.Execute(null);
            Assert.Equal(ShellPage.Conflicts, navigation.Current);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            page.TakeOursCommand.Execute(FirstConflict(page));
            await page.SaveCommand.ExecuteAsync(null);
            await page.CommitMergeCommand.ExecuteAsync(null);

            // The merge is over: the page goes away, and the rail does not leave the reader on it.
            Assert.DoesNotContain(navigation.Service.Items, item => item.Header == "Conflicts");
            Assert.NotEqual(ShellPage.Conflicts, navigation.Current);
        });
    }

    [Fact]
    public void TheBannerCountsWhatIsLeftAndGatesTheCommitButton()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();

            await ConflictAsync(repository, services);
            await OpenAsync(services);

            Assert.Equal("0 of 1 file resolved", shell.Conflicts.Progress);
            Assert.False(shell.Conflicts.CommitMergeCommand.CanExecute(null));

            shell.Conflicts.TakeOursCommand.Execute(FirstConflict(shell.Conflicts));
            await shell.Conflicts.SaveCommand.ExecuteAsync(null);

            Assert.Equal("1 of 1 file resolved", shell.Conflicts.Progress);
            Assert.True(shell.Conflicts.CommitMergeCommand.CanExecute(null));
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void ThePageRendersItsThreePanesAndItsPreview()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);
            page.TakeOursCommand.Execute(FirstConflict(page));

            ConflictResolutionPageView view = services.Get<ConflictResolutionPageView>();
            view.DataContext = page;

            Window window = new() { Width = 1400, Height = 900, Content = view };
            window.Show();

            try
            {
                window.Measure(new Size(1400, 900));
                window.Arrange(new Rect(0, 0, 1400, 900));
                window.UpdateLayout();

                List<TextBlock> labels = [.. view.GetVisualDescendants().OfType<TextBlock>()];

                // The three panes are the point of the page: each one is labelled, in order.
                Assert.Contains(labels, label => label.Text == "Ours");
                Assert.Contains(labels, label => label.Text == "Original");
                Assert.Contains(labels, label => label.Text == "Theirs");

                // Every line of the conflict reached the visual tree.
                List<string> lines =
                [
                    .. view.GetVisualDescendants()
                        .OfType<Enigma.GitClient.App.Controls.Diff.DiffLineText>()
                        .Select(line => line.Text ?? string.Empty),
                ];

                Assert.Contains("our version", lines);
                Assert.Contains("their version", lines);
                Assert.Contains("two", lines);

                // The read-only one: the other box with the same look is the region editor.
                TextBox preview = view.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(box => box.IsReadOnly);

                Assert.Equal("one\nour version\nthree\n", preview.Text);
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void ThePageActuallyPaintsInBothVariants()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictTwiceAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);
            page.TakeOursCommand.Execute(page.Regions.First(region => region.IsConflicted));

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
                {
                    application.RequestedThemeVariant = variant;

                    ConflictResolutionPageView view = services.Get<ConflictResolutionPageView>();
                    view.DataContext = page;

                    Window window = new() { Width = 1280, Height = 800, Content = view };

                    int colours = Render(window, $"conflict-page-{variant.ToString()!.ToLowerInvariant()}.png");

                    // A page that lays out and paints nothing looks identical to a correct one from
                    // every angle except this one.
                    Assert.True(
                        colours >= 8,
                        $"{variant}: the frame holds only {colours.ToString(System.Globalization.CultureInfo.InvariantCulture)} distinct colours");
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void TheRegionEditorPaintsAcrossTheWholeCard()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await ConflictTwiceAsync(repository, services);

            ConflictResolutionPageViewModel page = await OpenAsync(services);

            ConflictRegionViewModel region = FirstConflict(page);
            page.EditRegionCommand.Execute(region);

            ConflictResolutionPageView view = services.Get<ConflictResolutionPageView>();
            view.DataContext = page;

            Window window = new() { Width = 1280, Height = 800, Content = view };

            double width = 0;

            int colours = Render(window, "conflict-page-editing.png", _ =>
            {
                // Measured while the window is still up: every card carries an editor, and only the
                // region being edited has one on screen.
                width = view.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Where(box => !box.IsReadOnly && box.IsEffectivelyVisible)
                    .Max(box => box.Bounds.Width);
            });

            Assert.True(colours >= 8);

            // The box is the width of the card, not a column of it: a DockPanel child with no dock
            // goes to the left edge and keeps its own width, which is how that goes wrong.
            Assert.True(
                width > 600,
                $"the editor is only {width.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} wide");
        });
    }

    /// <summary>
    /// Shows the window, renders it until a frame holds real content, and saves that frame.
    /// </summary>
    /// <param name="inspect">Run while the window is still up, for anything measured on screen.</param>
    /// <returns>How many distinct colours the frame holds, quantised to 4 bits per channel.</returns>
    private static int Render(Window window, string fileName, Action<Window>? inspect = null)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName);

        window.Show();

        int best = 0;

        try
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                window.Measure(new Size(window.Width, window.Height));
                window.Arrange(new Rect(0, 0, window.Width, window.Height));
                window.UpdateLayout();
                window.InvalidateVisual();

                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                Dispatcher.UIThread.RunJobs();

                using Bitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The window produced no rendered frame.");

                frame.Save(path, PngBitmapEncoderOptions.Default);

                best = Math.Max(best, CountColours(path));

                if (best >= 8)
                {
                    break;
                }
            }

            inspect?.Invoke(window);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }

        return best;
    }

    private static unsafe int CountColours(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap writeable = WriteableBitmap.Decode(stream);
        using ILockedFramebuffer buffer = writeable.Lock();

        HashSet<int> colours = [];

        byte* pixels = (byte*)buffer.Address;

        for (int y = 0; y < buffer.Size.Height; y++)
        {
            byte* row = pixels + (y * buffer.RowBytes);

            for (int x = 0; x < buffer.Size.Width; x++)
            {
                colours.Add(((row[(x * 4) + 2] >> 4) << 8) | ((row[(x * 4) + 1] >> 4) << 4) | (row[(x * 4) + 0] >> 4));
            }
        }

        return colours.Count;
    }

    [Fact]
    public void TheTwoSidesAreTintedDifferentlyInBothVariants()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
                {
                    application.RequestedThemeVariant = variant;

                    Color ours = Resolve(application, "ConflictOursLineBrush", variant);
                    Color theirs = Resolve(application, "ConflictTheirsLineBrush", variant);
                    Color baseline = Resolve(application, "ConflictBaseLineBrush", variant);
                    Color foreground = Resolve(application, "EnigmaForegroundBrush", variant);

                    // Three tints that have to be told apart at a glance, or the panes say nothing.
                    Assert.NotEqual(ours, theirs);
                    Assert.NotEqual(ours, baseline);
                    Assert.NotEqual(theirs, baseline);

                    foreach ((string name, Color tint) in new[]
                    {
                        ("ours", ours), ("theirs", theirs), ("base", baseline),
                    })
                    {
                        double ratio = Contrast(foreground, tint);

                        Assert.True(
                            ratio >= 4.5,
                            $"{variant}/{name}: the text over the tint is only {ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1");
                    }

                    // And the headings that name them keep their own hue readable.
                    foreach (string key in new[]
                    {
                        "ConflictOursAccentBrush", "ConflictTheirsAccentBrush", "ConflictBaseAccentBrush",
                        "ConflictResolvedForegroundBrush", "ConflictPendingForegroundBrush",
                    })
                    {
                        Assert.NotEqual(default, Resolve(application, key, variant));
                    }
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    private static Color Resolve(Application application, string key, ThemeVariant variant)
    {
        Assert.True(application.TryFindResource(key, variant, out object? found), $"{key} must resolve under {variant}");

        return Assert.IsType<SolidColorBrush>(found).Color;
    }

    private static double Contrast(Color first, Color second)
    {
        double one = Luminance(first);
        double two = Luminance(second);

        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            double normalised = value / 255.0;

            return normalised <= 0.03928
                ? normalised / 12.92
                : Math.Pow((normalised + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
    }
}
