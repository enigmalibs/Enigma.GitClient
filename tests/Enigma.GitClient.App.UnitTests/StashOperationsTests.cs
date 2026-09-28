using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Stashes;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The stash operations every page shares, against real git: what they ask, what they do, and what
/// they say — above all when a stash comes back with conflicts.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class StashOperationsTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public StashOperationsTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- stashing

    [Fact]
    public void Stash_UsesTheMessageTypedInTheDialog_AndTakesTheUntrackedFiles()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);

            Write(repository, "app.txt", "edited\n");
            Write(repository, "new.txt", "untracked\n");

            services.Dialogs.OnShown = dialog =>
                ((StashDialogViewModel)((StashDialogView)dialog.Content!).DataContext!).Message = "  Half-done refactor  ";
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<IStashOperations>().StashAsync());

            StashEntry entry = Assert.Single(await ListAsync(services, repository));
            Assert.Equal("Half-done refactor", entry.Message);
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "new.txt")));
            Assert.Equal(InfoBarSeverity.Success, services.InfoBar.Last!.Severity);
            Assert.Equal("Stash", services.Dialogs.Last!.PrimaryButtonText);
        });
    }

    [Fact]
    public void Stash_WithoutAMessage_IsNamedByGit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);

            Write(repository, "app.txt", "edited\n");
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<IStashOperations>().StashAsync());

            StashEntry entry = Assert.Single(await ListAsync(services, repository));
            Assert.StartsWith(Git(repository, "rev-parse", "--short", "HEAD"), entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Stash_Cancelled_StashesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);

            Write(repository, "app.txt", "edited\n");
            services.Dialogs.Result = DialogResult.Close;

            Assert.False(await services.Get<IStashOperations>().StashAsync());
            Assert.Empty(await ListAsync(services, repository));
            Assert.Equal("edited\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")));
        });
    }

    [Fact]
    public void Stash_WithNothingToStash_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);

            services.Dialogs.Result = DialogResult.Primary;

            Assert.False(await services.Get<IStashOperations>().StashAsync());
            Assert.Equal("Nothing to stash", services.InfoBar.Last!.Title);
            Assert.Empty(await ListAsync(services, repository));
        });
    }

    // ---------------------------------------------------------------- getting it back

    [Fact]
    public void Pop_WithoutConflicts_BringsTheChangesBack_AndRemovesTheStash()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);
            StashEntry entry = await StashAsync(services, repository, "app.txt", "stashed\n", "work");

            Assert.True(await services.Get<IStashOperations>().PopAsync(entry));

            Assert.Equal("stashed\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")));
            Assert.Empty(await ListAsync(services, repository));
            Assert.Equal("Stash popped", services.InfoBar.Last!.Title);
        });
    }

    [Fact]
    public void Pop_WithConflicts_KeepsTheStash_AndWarns()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);
            StashEntry entry = await StashAsync(services, repository, "app.txt", "stashed\n", "work");

            Write(repository, "app.txt", "committed\n");
            Git(repository, "commit", "-am", "Change the same line");

            Assert.True(await services.Get<IStashOperations>().PopAsync(entry));

            RecordedNotification told = services.InfoBar.Last!;
            Assert.Equal(InfoBarSeverity.Warning, told.Severity);
            Assert.Equal("The stash applied with conflicts", told.Title);
            Assert.Contains("the stash is kept", told.Message, StringComparison.Ordinal);

            Assert.Single(await ListAsync(services, repository));
            Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Pop_ThatGitRefuses_ChangesNothing_AndReportsWhy()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);
            StashEntry entry = await StashAsync(services, repository, "app.txt", "stashed\n", "work");

            Write(repository, "app.txt", "local edit\n");

            Assert.False(await services.Get<IStashOperations>().PopAsync(entry));

            RecordedNotification told = services.InfoBar.Last!;
            Assert.Equal(InfoBarSeverity.Error, told.Severity);
            Assert.Equal("Could not pop the stash", told.Title);
            Assert.Contains("would be overwritten", told.Message, StringComparison.Ordinal);

            Assert.Equal("local edit\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")));
            Assert.Single(await ListAsync(services, repository));
        });
    }

    [Fact]
    public void Apply_BringsTheChangesBack_AndAlwaysKeepsTheStash()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);
            StashEntry entry = await StashAsync(services, repository, "app.txt", "stashed\n", "work");

            Assert.True(await services.Get<IStashOperations>().ApplyAsync(entry));

            Assert.Equal("stashed\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")));
            Assert.Single(await ListAsync(services, repository));
            Assert.Equal("Stash applied", services.InfoBar.Last!.Title);
        });
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public void Delete_AsksFirst_WithTheHarmlessButtonAsDefault_AndACancelKeepsTheStash()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);
            StashEntry entry = await StashAsync(services, repository, "app.txt", "stashed\n", "keep me");

            services.Dialogs.Result = DialogResult.Close;

            Assert.False(await services.Get<IStashOperations>().DropAsync(entry));

            ContentDialog asked = services.Dialogs.Last!;
            Assert.Equal("Delete stash", asked.Title);
            Assert.Equal(DefaultButton.Close, asked.DefaultButton);
            Assert.Contains("keep me", Assert.IsType<string>(asked.Content), StringComparison.Ordinal);
            Assert.Single(await ListAsync(services, repository));

            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<IStashOperations>().DropAsync(entry));
            Assert.Empty(await ListAsync(services, repository));
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<RepositoryHandle> OpenAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "stash-operations"), "main");

        Write(repository, "app.txt", "one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the application");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        return repository;
    }

    private static async Task<StashEntry> StashAsync(
        TestServices services,
        RepositoryHandle repository,
        string path,
        string content,
        string message)
    {
        Write(repository, path, content);
        Git(repository, "stash", "push", "-m", message);

        return (await ListAsync(services, repository))[0];
    }

    private static Task<IReadOnlyList<StashEntry>> ListAsync(TestServices services, RepositoryHandle repository)
        => services.Get<IStashService>().ListAsync(repository);

    private static void Write(RepositoryHandle repository, string path, string content)
        => File.WriteAllText(Path.Combine(repository.WorkTreePath, path), content);

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
