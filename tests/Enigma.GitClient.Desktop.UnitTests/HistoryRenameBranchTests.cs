using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A local branch's badge in the history renames it: the item in its menu, the dialog the branches
/// page asks with, and the history drawn again under the new name. Against real git, with a bare
/// <c>origin</c>.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryRenameBranchTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryRenameBranchTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void ALocalBranchsBadge_OffersRename_RightBeforeDelete()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");
            (Window window, HistoryPageView view) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = Badge(view, "topic");
                HistoryBranchViewModel topic = Assert.IsType<HistoryBranchViewModel>(badge.DataContext);
                Assert.True(topic.CanRename);

                ContextMenu menu = badge.ContextMenu!;
                menu.Open(badge);
                Dispatcher.UIThread.RunJobs();

                MenuItem[] shown = [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible)];

                MenuItem rename = shown.Single(item => (item.Header as string) == "Rename \"topic\"…");
                MenuItem delete = shown.Single(item => (item.Header as string) == "Delete \"topic\"…");

                Assert.Equal(Array.IndexOf(shown, rename) + 1, Array.IndexOf(shown, delete));
                Assert.Same(topic.Commands.Rename, rename.Command);
                Assert.Same(topic, rename.CommandParameter);
                Assert.Equal(PhosphorIcon.PencilSimple, Assert.IsType<Icon>(rename.Icon).Kind);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ARemoteBranchsBadge_DoesNotOfferRename()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            // Only on the remote: pushed, then deleted here.
            Git(repository, "branch", "topic");
            Git(repository, "push", "origin", "topic");
            Git(repository, "branch", "-D", "topic");
            (Window window, HistoryPageView view) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = Badge(view, "origin/topic");
                HistoryBranchViewModel remote = Assert.IsType<HistoryBranchViewModel>(badge.DataContext);
                Assert.True(remote.IsRemote);
                Assert.False(remote.CanRename);
                Assert.False(remote.Commands.Rename!.CanExecute(remote));

                ContextMenu menu = badge.ContextMenu!;
                menu.Open(badge);
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(
                    menu.Items.OfType<MenuItem>().Where(item => item.IsVisible),
                    item => (item.Header as string)?.StartsWith("Rename", StringComparison.Ordinal) == true);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- renaming

    [Fact]
    public void ConfirmingTheDialog_RenamesTheBranch_AndTheHistoryDrawsTheNewName()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");

            HistoryBranchViewModel topic = (await LineAsync(services, repository, "Add the readme"))
                .Branches.Single(branch => branch.Name == "topic");

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RenameBranchDialogViewModel model })
                {
                    model.Name = "feature/renamed";
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await topic.Commands.Rename!.ExecuteAsync(topic);

            // The branches page's dialog, as the branches page asks it.
            ContentDialog asked = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Rename branch", asked.Title);
            Assert.Equal("Rename", asked.PrimaryButtonText);
            Assert.Equal("topic", Assert.IsType<RenameBranchDialogViewModel>(((Control)asked.Content!).DataContext).CurrentName);

            Assert.Equal(string.Empty, Git(repository, "branch", "--list", "topic"));
            Assert.Equal("feature/renamed", Git(repository, "branch", "--list", "feature/renamed").TrimStart('*', ' '));

            string[] drawn = [.. Line(services, "Add the readme").Branches.Select(branch => branch.Name)];
            Assert.Contains("feature/renamed", drawn);
            Assert.DoesNotContain("topic", drawn);
        });
    }

    [Fact]
    public void RenamingTheCurrentBranch_KeepsHeadOnIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            HistoryBranchViewModel main = (await LineAsync(services, repository, "Add the readme"))
                .Branches.Single(branch => branch.Name == "main");
            Assert.True(main.IsCurrent);
            Assert.True(main.Commands.Rename!.CanExecute(main));

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RenameBranchDialogViewModel model })
                {
                    model.Name = "trunk";
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await main.Commands.Rename.ExecuteAsync(main);

            Assert.Equal("trunk", Git(repository, "symbolic-ref", "--short", "HEAD"));
            Assert.Equal("trunk", services.Get<IRepositoryContext>().Head?.BranchName);

            HistoryBranchViewModel trunk = Line(services, "Add the readme").Branches.Single(branch => branch.Name == "trunk");
            Assert.True(trunk.IsCurrent);
        });
    }

    [Fact]
    public void CancellingTheDialog_RenamesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");

            HistoryBranchViewModel topic = (await LineAsync(services, repository, "Add the readme"))
                .Branches.Single(branch => branch.Name == "topic");

            // A name typed, then Cancel.
            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RenameBranchDialogViewModel model })
                {
                    model.Name = "renamed";
                }
            };

            services.Dialogs.Result = DialogResult.None;

            await topic.Commands.Rename!.ExecuteAsync(topic);

            Assert.Single(services.Dialogs.Shown);
            Assert.Equal("topic", Git(repository, "branch", "--list", "topic").TrimStart('*', ' '));
            Assert.Equal(string.Empty, Git(repository, "branch", "--list", "renamed"));
        });
    }

    // ---------------------------------------------------------------- the dialog

    [Fact]
    public void TheRenameDialog_OpensWithTheWholeNameSelected()
    {
        _fixture.RunAsync(async () =>
        {
            // The dialog on a real host, opened from a control that had the focus — the badge's menu,
            // as it is in the window.
            Button opener = new() { Content = "Rename" };
            ContentDialog host = new();
            Window window = new() { Content = new Panel { Children = { opener, host } }, Width = 1200, Height = 800 };
            window.Show();

            ContentDialogService dialogs = new();
            dialogs.RegisterHost(host);

            opener.Focus();
            Assert.True(opener.IsFocused);

            RenameBranchDialogView view = new() { DataContext = new RenameBranchDialogViewModel("topic", ["main", "topic"]) };

            try
            {
                Task<DialogResult> asking = dialogs.ShowAsync(dialog =>
                {
                    dialog.Title = "Rename branch";
                    dialog.Content = view;
                    dialog.PrimaryButtonText = "Rename";
                    dialog.CloseButtonText = "Cancel";
                });

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }

                TextBox name = view.GetLogicalDescendants()
                    .OfType<TextBox>()
                    .Single(box => global::Avalonia.Automation.AutomationProperties.GetName(box) == "New branch name");

                Assert.True(name.IsFocused, "the name box does not have the focus: the new name cannot be typed straight away");
                Assert.False(opener.IsFocused);
                Assert.Equal("topic", name.SelectedText);

                await host.HideAsync();
                await asking;
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A repository whose one commit is on <c>main</c>, published to a bare <c>origin</c> that
    /// <c>main</c> tracks.
    /// </summary>
    private static async Task<RepositoryHandle> BuildPublishedAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "local"), "main");

        Commit(repository, "README.md", "# one\n", "Add the readme");

        string origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(origin);
        Git(repository, "init", "--bare", origin);
        Git(repository, "remote", "add", "origin", origin);
        Git(repository, "push", "--set-upstream", "origin", "main");

        return repository;
    }

    private static async Task<CommitRowViewModel> LineAsync(TestServices services, RepositoryHandle repository, string subject)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return Line(services, subject);
    }

    private static CommitRowViewModel Line(TestServices services, string subject)
        => services.Get<HistoryPageViewModel>().Rows.Single(row => row.Subject == subject);

    private static async Task<(Window Window, HistoryPageView View)> ShowAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return (window, view);
    }

    private static RefBadge Badge(HistoryPageView view, string name)
        => view.GetVisualDescendants()
            .OfType<RefBadge>()
            .First(badge => badge.DataContext is HistoryBranchViewModel branch && branch.Name == name);

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static string Git(RepositoryHandle repository, params string[] arguments)
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
            ? output.Trim()
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}
