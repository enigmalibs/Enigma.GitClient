using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Submodules follow the pin: the step every write ends with — what it notes before, when it follows,
/// what it says — the setting that turns it off, the band that says a submodule is behind, and the clone
/// box that starts ticked. Against a fake submodule service: the follow itself is Core's, tested there.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SubmoduleFollowTests
{
    private const string Before = "1111111111111111111111111111111111111111";
    private const string After = "2222222222222222222222222222222222222222";

    private readonly HeadlessAvaloniaFixture _fixture;

    public SubmoduleFollowTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices Build(FakeSubmoduleService fake)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<ISubmoduleService>();
            services.AddSingleton<ISubmoduleService>(fake);
        });

    /// <summary>A folder to open as the repository, with a <c>.gitmodules</c> or without.</summary>
    private static RepositoryHandle Folder(TestServices services, bool withSubmodules)
    {
        string folder = Path.Combine(services.ConfigurationRoot, withSubmodules ? "super" : "plain");
        Directory.CreateDirectory(folder);

        if (withSubmodules)
        {
            File.WriteAllText(Path.Combine(folder, ".gitmodules"), "[submodule \"libs/a\"]\n\tpath = libs/a\n\turl = ../a\n");
        }

        return new RepositoryHandle(folder, Path.Combine(folder, ".git"));
    }

    private static GitSubmodule Submodule(string path, SubmoduleCheckoutState state)
        => new()
        {
            Name = path,
            Path = path,
            FullPath = path,
            ExpectedSha = After,
            CheckedOutSha = state == SubmoduleCheckoutState.NotDownloaded ? null : Before,
            State = state,
        };

    private static Task WriteAsync(IRepositoryContext context, bool followSubmodules = true)
        => context.RunExclusiveAsync((_, _) => Task.CompletedTask, refreshAfter: true, CancellationToken.None, followSubmodules);

    // ---------------------------------------------------------------- the step around every write

    [Fact]
    public void AWriteThatMovedHead_FollowsFromWhereHeadWasToWhereItIs()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            fake.Heads.Enqueue(Before);
            fake.Heads.Enqueue(After);
            using TestServices services = Build(fake);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(Folder(services, withSubmodules: true));

            await WriteAsync(context);

            Assert.Equal([(Before, After)], fake.Follows);
        });
    }

    [Fact]
    public void AWriteThatDidNotMoveHead_FollowsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            fake.Heads.Enqueue(Before);
            using TestServices services = Build(fake);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(Folder(services, withSubmodules: true));

            await WriteAsync(context);

            Assert.Empty(fake.Follows);
        });
    }

    [Fact]
    public void WithTheSettingOff_NothingFollows()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            fake.Heads.Enqueue(Before);
            fake.Heads.Enqueue(After);
            using TestServices services = Build(fake);
            services.Get<ISettingsService>().Update(current => current with { UpdateSubmodulesAutomatically = false });
            IRepositoryContext context = services.Get<IRepositoryContext>();
            RepositoryHandle repository = Folder(services, withSubmodules: true);
            await context.OpenAsync(repository);

            Assert.Null(await services.Get<ISubmoduleFollowStep>().BeforeAsync(repository, Before, CancellationToken.None));

            await WriteAsync(context);

            Assert.Empty(fake.Follows);
        });
    }

    [Fact]
    public void AWriteThatOptsOut_FollowsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            fake.Heads.Enqueue(Before);
            fake.Heads.Enqueue(After);
            using TestServices services = Build(fake);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(Folder(services, withSubmodules: true));

            await WriteAsync(context, followSubmodules: false);

            Assert.Empty(fake.Follows);
        });
    }

    [Fact]
    public void WithoutGitModules_TheMarkIsTheKnownHead_AndNothingRuns()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            fake.Heads.Enqueue(After);
            using TestServices services = Build(fake);
            RepositoryHandle repository = Folder(services, withSubmodules: false);
            ISubmoduleFollowStep step = services.Get<ISubmoduleFollowStep>();

            SubmoduleFollowMark? mark = await step.BeforeAsync(repository, Before, CancellationToken.None);

            Assert.Equal(new SubmoduleFollowMark(Before), mark);
            Assert.Null(await step.AfterAsync(repository, mark!, CancellationToken.None));
            Assert.Empty(fake.Follows);
        });
    }

    [Fact]
    public void AFollowThatLeftSubmodulesBehind_NamesThemInAWarning()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new()
            {
                FollowResult = new SubmoduleUpdateResult(
                    ["libs/a"],
                    [new SubmoduleSkip("libs/b", SubmoduleSkipReason.UncommittedChanges)],
                    []),
            };
            fake.Heads.Enqueue(Before);
            fake.Heads.Enqueue(After);
            using TestServices services = Build(fake);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(Folder(services, withSubmodules: true));

            await WriteAsync(context);

            RecordedNotification warning = services.InfoBar.Shown[^1];
            Assert.Equal(InfoBarSeverity.Warning, warning.Severity);
            Assert.Equal("A submodule was not updated", warning.Title);
            Assert.Contains("libs/b has uncommitted changes.", warning.Message, StringComparison.Ordinal);
            Assert.False(services.Overlay.IsOpen);
        });
    }

    // ---------------------------------------------------------------- the band

    [Fact]
    public void TheBand_ShowsForASubmoduleBehindOrNotDownloaded_AndNotForWorkInProgress()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new()
            {
                Submodules =
                [
                    Submodule("libs/behind", SubmoduleCheckoutState.Behind),
                    Submodule("libs/ahead", SubmoduleCheckoutState.NewCommits),
                    Submodule("libs/empty", SubmoduleCheckoutState.NotDownloaded),
                    Submodule("libs/side", SubmoduleCheckoutState.Elsewhere),
                ],
            };
            using TestServices services = Build(fake);
            await services.Get<IRepositoryContext>().OpenAsync(Folder(services, withSubmodules: true));
            SubmoduleNoticeViewModel band = services.Get<SubmoduleNoticeViewModel>();

            await band.ReadAsync();

            Assert.True(band.IsVisible);
            Assert.Equal("2 submodules are not on the commit this repository expects.", band.Message);
            Assert.Contains("libs/behind: behind", band.Details, StringComparison.Ordinal);
            Assert.Contains("libs/empty: not downloaded", band.Details, StringComparison.Ordinal);

            await band.UpdateCommand.ExecuteAsync(null);
            Assert.Equal(["libs/behind", "libs/empty"], Assert.Single(fake.Updates)!);

            fake.Submodules = [Submodule("libs/ahead", SubmoduleCheckoutState.NewCommits), Submodule("libs/side", SubmoduleCheckoutState.Elsewhere)];
            await band.ReadAsync();

            Assert.False(band.IsVisible);
            Assert.False(band.UpdateCommand.CanExecute(null));
        });
    }

    [Fact]
    public void WithoutGitModules_TheBandReadsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new() { Submodules = [Submodule("libs/behind", SubmoduleCheckoutState.Behind)] };
            using TestServices services = Build(fake);
            await services.Get<IRepositoryContext>().OpenAsync(Folder(services, withSubmodules: false));
            SubmoduleNoticeViewModel band = services.Get<SubmoduleNoticeViewModel>();
            int before = fake.Lists;

            await band.ReadAsync();

            Assert.False(band.IsVisible);
            Assert.Equal(before, fake.Lists);
        });
    }

    [Fact]
    public void TheBandSaysOneSubmoduleInTheSingular()
        => Assert.Equal("1 submodule is not on the commit this repository expects.", SubmoduleNoticeViewModel.Describe(1));

    // ---------------------------------------------------------------- the clone box

    [Fact]
    public void TheCloneDialog_ClonesSubmodulesByDefault()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            CloneRepositoryDialogViewModel? clone = null;
            services.Dialogs.OnShown = dialog => clone ??= ((Control)dialog.Content!).DataContext as CloneRepositoryDialogViewModel;

            await services.Get<RepositoriesPageViewModel>().CloneCommand.ExecuteAsync(null);

            Assert.NotNull(clone);
            Assert.True(clone.RecurseSubmodules);
        });
    }

    [Fact]
    public void TheSettingsPage_ShowsAndChangesTheSetting()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();
            await page.OnAppearingAsync();

            Assert.True(page.UpdateSubmodulesAutomatically);

            page.UpdateSubmodulesAutomatically = false;

            Assert.False(services.Get<ISettingsService>().Current.UpdateSubmodulesAutomatically);
        });
    }
}
