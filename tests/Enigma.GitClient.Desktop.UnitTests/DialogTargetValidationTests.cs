using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The clone and create dialogs check their target on disk off the UI thread (CODE-REVIEW-6E56
/// PERF-03): a short while after the last change, once per change, never from a bound getter, and
/// with a folder that cannot be listed said rather than thrown.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DialogTargetValidationTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DialogTargetValidationTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void Keystrokes_TouchNoDisk_UntilTheDelayHasPassed_ThenOneProbeChecksTheLastTarget()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            ManualTimeProvider clock = new();
            ConcurrentQueue<string> probed = new();

            InitRepositoryDialogViewModel model = new(
                services.Get<IFolderDialogService>(),
                services.ConfigurationRoot,
                clock,
                target =>
                {
                    probed.Enqueue(target);
                    return string.Empty;
                });

            model.DirectoryName = "a";
            model.DirectoryName = "ab";
            model.DirectoryName = "abc";

            Assert.Empty(probed);
            Assert.False(model.IsValid);

            clock.Advance(DebouncedProbe<string>.Delay);
            await model.ValidateNowAsync();

            Assert.Equal([Path.Combine(services.ConfigurationRoot, "abc")], probed);
            Assert.True(model.IsValid);

            // The next change: one more probe.
            model.DirectoryName = "abcd";
            await model.ValidateNowAsync();

            Assert.Equal(2, probed.Count);
            Assert.Equal(Path.Combine(services.ConfigurationRoot, "abcd"), probed.Last());
        });
    }

    [Fact]
    public void AFieldProblem_IsSaidAtOnce_AndNoProbeRuns()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            int probes = 0;

            InitRepositoryDialogViewModel model = new(
                services.Get<IFolderDialogService>(),
                services.ConfigurationRoot,
                new ManualTimeProvider(),
                _ =>
                {
                    probes++;
                    return string.Empty;
                })
            {
                DirectoryName = "bad|name",
            };

            await model.ValidateNowAsync();

            Assert.Equal(0, probes);
            Assert.False(model.IsValid);
            Assert.Contains("characters the file system does not allow", model.ValidationMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void WhileTheDiskIsChecked_TheDialogCannotBeConfirmed_AndSaysNothingStale()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            string occupied = Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "occupied")).FullName;
            File.WriteAllText(Path.Combine(occupied, "file.txt"), "content");
            Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "empty"));

            CloneRepositoryDialogViewModel model = new(
                services.Get<IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot)
            {
                Url = "https://github.com/owner/repository.git",
                DirectoryName = "occupied",
            };

            await model.ValidateNowAsync();
            Assert.Contains("not empty", model.ValidationMessage, StringComparison.Ordinal);

            int changes = 0;
            model.ValidationChanged += (_, _) => changes++;
            model.DirectoryName = "empty";

            // Pending: not confirmable, and the message about the other folder is gone.
            Assert.False(model.IsValid);
            Assert.False(model.HasValidationMessage);
            Assert.Equal(1, changes);

            await model.ValidateNowAsync();

            Assert.True(model.IsValid);
            Assert.False(model.HasValidationMessage);
            Assert.Equal(2, changes);
        });
    }

    [Fact]
    public void ATargetThatCannotBeListed_GivesAMessage_NotAnException()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            string locked = Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "locked")).FullName;
            File.WriteAllText(Path.Combine(locked, "inside.txt"), "content");

            // Disposed before the container, which deletes the directory.
            using IDisposable unlock = DenyListing(locked);
            Assert.SkipUnless(CannotList(locked), "This account can list any directory (root, or an administrator).");

            CloneRepositoryDialogViewModel model = new(
                services.Get<IFolderDialogService>(),
                services.Get<IRepositoryService>(),
                services.ConfigurationRoot)
            {
                Url = "https://github.com/owner/repository.git",
                DirectoryName = "locked",
            };

            await model.ValidateNowAsync();

            Assert.False(model.IsValid);
            Assert.Equal($"'{Path.Combine(services.ConfigurationRoot, "locked")}' exists but cannot be read.", model.ValidationMessage);
        });
    }

    [Fact]
    public void ConfirmedAtOnce_TheCreateDialogStillWaitsForTheCheck_AndCreatesNothingOverARepository()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            string root = Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "workspace")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "already", ".git"));

            // Filled and confirmed in the same moment, before any disk check has answered.
            services.Dialogs.Result = DialogResult.Primary;
            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: InitRepositoryDialogViewModel model })
                {
                    model.ParentDirectory = root;
                    model.DirectoryName = "already";
                }
            };

            await services.Get<RepositoriesPageViewModel>().CreateCommand.ExecuteAsync(null);

            Assert.Empty(services.InfoBar.Shown);
            Assert.Empty(services.Windows.Requested);
        });
    }

    private static bool CannotList(string path)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Any();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Takes the right to list a directory from the current account, and gives it back on dispose.
    /// </summary>
    private static IDisposable DenyListing(string path)
        => OperatingSystem.IsWindows() ? DenyListingOnWindows(path) : DenyListingOnUnix(path);

    [SupportedOSPlatform("windows")]
    private static IDisposable DenyListingOnWindows(string path)
    {
        DirectoryInfo directory = new(path);
        DirectorySecurity security = directory.GetAccessControl();

        if (WindowsIdentity.GetCurrent().User is not { } user)
        {
            return new Restore(() => { });
        }

        FileSystemAccessRule rule = new(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);

        return new Restore(() =>
        {
            DirectorySecurity current = directory.GetAccessControl();
            current.RemoveAccessRule(rule);
            directory.SetAccessControl(current);
        });
    }

    [UnsupportedOSPlatform("windows")]
    private static IDisposable DenyListingOnUnix(string path)
    {
        UnixFileMode mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);

        return new Restore(() => File.SetUnixFileMode(path, mode));
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
