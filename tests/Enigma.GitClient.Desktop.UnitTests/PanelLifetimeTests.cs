using System;
using System.IO;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The diff viewer and the changed-files list follow the settings while they live, and let go of them
/// when their owner disposes them (SOLID-04): the settings outlive every panel built on them.
/// </summary>
public sealed class PanelLifetimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "enigma-lifetime-" + Guid.NewGuid().ToString("N"));

    // A debounce no test waits out: nothing is written, and disposing the service drops the write.
    private readonly SettingsService _settings;

    public PanelLifetimeTests()
        => _settings = new SettingsService(new AppPaths(_root), NullLogger<SettingsService>.Instance, TimeSpan.FromHours(1));

    public void Dispose()
    {
        _settings.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ADisposedViewer_NoLongerFollowsTheSettings()
    {
        DiffViewerViewModel viewer = new(
            new RecordingDiffService(),
            new RecordingSystemInterop(),
            _settings,
            NullLogger<DiffViewerViewModel>.Instance);

        _settings.Update(current => current with { DiffView = DiffView.Unified });
        Assert.Equal(DiffViewMode.Unified, viewer.ViewMode);

        viewer.Dispose();
        _settings.Update(current => current with { DiffView = DiffView.SideBySide });

        Assert.Equal(DiffViewMode.Unified, viewer.ViewMode);
    }

    [Fact]
    public void ADisposedPanel_NoLongerFollowsTheSettings()
    {
        ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop(), _settings);

        _settings.Update(current => current with { FilesView = FilesView.List });
        Assert.Equal(ChangedFilesViewMode.List, panel.ViewMode);

        panel.Dispose();
        _settings.Update(current => current with { FilesView = FilesView.Tree });

        Assert.Equal(ChangedFilesViewMode.List, panel.ViewMode);
    }

    [Fact]
    public void TheWorkingTreePanel_DisposesBothOfItsLists()
    {
        using TestServices services = TestServices.Build();
        WorkingTreePanelViewModel workingTree = services.Get<WorkingTreePanelViewModel>();
        ISettingsService settings = services.Get<ISettingsService>();

        settings.Update(current => current with { FilesView = FilesView.List });
        workingTree.Dispose();
        settings.Update(current => current with { FilesView = FilesView.Tree });

        Assert.Equal(ChangedFilesViewMode.List, workingTree.Unstaged.ViewMode);
        Assert.Equal(ChangedFilesViewMode.List, workingTree.Staged.ViewMode);
    }

    [Fact]
    public void TheHistoryPagesViewer_IsItsOwn_NotOneTheContainerKeeps()
    {
        using TestServices services = TestServices.Build();
        Func<DiffViewerViewModel> viewers = services.Get<Func<DiffViewerViewModel>>();

        // One per call: a viewer is per consumer, and each is its owner's to dispose.
        Assert.NotSame(viewers(), viewers());
    }
}
