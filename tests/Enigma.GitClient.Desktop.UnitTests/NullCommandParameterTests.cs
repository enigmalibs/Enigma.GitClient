using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A command given no row does nothing (CODE-REVIEW-6E56 STYLE-01). Its handler checks the row itself
/// instead of trusting a separate <c>CanExecute</c>, which a relay command's <c>Execute</c> does not
/// consult.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class NullCommandParameterTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public NullCommandParameterTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheBranchesDialogsRowCommands_DoNothingWithoutARow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();

            page.SelectInHistoryCommand.Execute(null);
            page.ToggleVisibilityCommand.Execute(null);
            await page.OpenWorktreeCommand.ExecuteAsync(null);
            await page.OpenWorktreeFolderCommand.ExecuteAsync(null);
            await page.OpenWorktreeTerminalCommand.ExecuteAsync(null);
            await page.RemoveWorktreeCommand.ExecuteAsync(null);

            Assert.Empty(services.Launcher.Launched);
            Assert.Empty(services.Interop.Copied);
        });
    }

    [Fact]
    public void TheWorktreesAndSubmodulesRowCommands_DoNothingWithoutARow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel worktrees = services.Get<WorktreesPageViewModel>();
            SubmodulesPageViewModel submodules = services.Get<SubmodulesPageViewModel>();

            await worktrees.OpenCommand.ExecuteAsync(null);
            await worktrees.OpenFolderCommand.ExecuteAsync(null);
            await worktrees.OpenTerminalCommand.ExecuteAsync(null);
            await submodules.OpenCommand.ExecuteAsync(null);
            await submodules.UpdateCommand.ExecuteAsync(null);
            await submodules.OpenFolderCommand.ExecuteAsync(null);
            await submodules.OpenTerminalCommand.ExecuteAsync(null);
            await submodules.CopyPathCommand.ExecuteAsync(null);

            Assert.Empty(services.Launcher.Launched);
            Assert.Empty(services.Interop.Copied);
        });
    }

    [Fact]
    public void CopyAndSelectInHistory_DoNothingWithNothingToCopyOrSelect()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            await services.Get<HistoryPageViewModel>().CopyCommand.ExecuteAsync(null);
            await services.Get<HistoryPageViewModel>().CopyCommand.ExecuteAsync(string.Empty);
            services.Get<TagsPageViewModel>().SelectInHistoryCommand.Execute(null);

            Assert.Empty(services.Interop.Copied);
        });
    }
}
