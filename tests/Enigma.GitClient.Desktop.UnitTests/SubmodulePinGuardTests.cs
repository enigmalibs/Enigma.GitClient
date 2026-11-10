using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Status;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// What the working changes say about submodules: the question a commit asks only when a pin goes back,
/// what a discard's question adds for each submodule, and a submodule line's badge and menu.
/// </summary>
public sealed class SubmodulePinGuardTests
{
    private const string A = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string B = "f00ba12345678901234567890123456789abcdef";

    private static PinChange Change(PinDirection direction, int removed = 0)
        => new("libs/Common", A, B, direction, [], 0, [], removed);

    [Fact]
    public void TheCommitGuard_AsksOnlyForAPinGoingBack()
    {
        Assert.Null(WorkingTreePanelViewModel.DescribeGoingBack([Change(PinDirection.Forward)]));
        Assert.Null(WorkingTreePanelViewModel.DescribeGoingBack([Change(PinDirection.Unknown)]));
        Assert.Null(WorkingTreePanelViewModel.DescribeGoingBack([Change(PinDirection.Diverged, removed: 2)]));
        Assert.Null(WorkingTreePanelViewModel.DescribeGoingBack([]));

        Assert.Equal(
            "This commit moves libs/Common back 3 commits (to f00ba12). Is that intended?",
            WorkingTreePanelViewModel.DescribeGoingBack([Change(PinDirection.Forward), Change(PinDirection.Back, removed: 3)]));
    }

    [Fact]
    public void ADiscardsQuestion_SaysWhereEachSubmoduleGoesBack()
    {
        Assert.Equal(string.Empty, WorkingTreePanelViewModel.DescribeSubmodules([]));
        Assert.Equal(string.Empty, WorkingTreePanelViewModel.DescribeSubmodules([new SubmoduleDiscard("libs/Common", A, A, false, 0)]));

        Assert.Equal(
            "\n\nlibs/Common goes back to a1b2c3d; 1 commit no branch contains is left behind.",
            WorkingTreePanelViewModel.DescribeSubmodules([new SubmoduleDiscard("libs/Common", A, B, false, 1)]));
    }

    [Fact]
    public void ASubmodulesLine_SaysWhatItHolds_AndOffersToOpenIt()
    {
        ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop(), settings: null, new RecordingInstanceLauncher()) { WorkTreePath = "/src/super" };
        ChangedFile file = new()
        {
            Path = "libs/Common",
            IsSubmodule = true,
            Submodule = new SubmoduleState(true, CommitChanged: true, HasModifiedTracked: true, HasUntracked: false),
        };

        ChangedFileNodeViewModel line = new(panel, file, "Common");

        Assert.True(line.IsSubmodule);
        Assert.Equal("other commit checked out, modified content", line.SubmoduleBadge);
        Assert.True(line.CanOpenSubmodule);
    }
}
