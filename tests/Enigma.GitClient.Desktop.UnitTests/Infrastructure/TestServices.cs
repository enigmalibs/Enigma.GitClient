using System;
using System.IO;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// Builds the application's container for a test, with the two things a test must never share with
/// the developer replaced: the git reference reader, and the per-user configuration directory.
/// </summary>
public sealed class TestServices : IDisposable
{
    private readonly ServiceProvider _provider;

    private TestServices(ServiceProvider provider, string configurationRoot)
    {
        _provider = provider;
        ConfigurationRoot = configurationRoot;
    }

    /// <summary>
    /// Gets the throwaway directory standing in for the user's configuration directory.
    /// </summary>
    public string ConfigurationRoot { get; }

    /// <summary>
    /// Gets the container.
    /// </summary>
    public IServiceProvider Provider => _provider;

    /// <summary>
    /// Resolves a required service.
    /// </summary>
    /// <typeparam name="TService">The service type.</typeparam>
    /// <returns>The resolved service.</returns>
    public TService Get<TService>()
        where TService : notnull
        => _provider.GetRequiredService<TService>();

    /// <summary>
    /// Builds a container for a test.
    /// </summary>
    /// <param name="useRealRefReader">
    /// Whether to keep the real <see cref="IRefReader"/>. Pass <see langword="true"/> for a test
    /// that works against a repository it actually created — a page showing branch badges or a HEAD
    /// marker is showing what the reader found, so faking the reader would test nothing.
    /// </param>
    /// <param name="configure">
    /// A last chance to replace a registration, for a test that must not let a real service reach
    /// outside the process — a hosting provider that would call an API, for instance.
    /// </param>
    /// <returns>The container, which must be disposed.</returns>
    public static TestServices Build(bool useRealRefReader = false, Action<ServiceCollection>? configure = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "enigma-app-tests-" + Guid.NewGuid().ToString("N"));

        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        App.ConfigureServices(services);

        if (!useRealRefReader)
        {
            services.RemoveAll<IRefReader>();
            services.AddSingleton<IRefReader, FakeRefReader>();
        }

        // Never the developer's own ~/.config: a test that writes there is a test that changes the
        // machine it runs on.
        services.RemoveAll<IAppPaths>();
        services.AddSingleton<IAppPaths>(_ => new AppPaths(root));

        // The three host-driven UI services are replaced rather than hosted. They throw until a
        // host is registered, and hosting the real animated controls outside a live visual tree
        // makes a test wait on a frame that never comes. Recording doubles turn "did the page
        // report this?" into an assertion instead.
        services.RemoveAll<IInfoBarService>();
        services.AddSingleton<IInfoBarService, RecordingInfoBarService>();
        services.RemoveAll<IOverlayService>();
        services.AddSingleton<IOverlayService, RecordingOverlayService>();
        services.RemoveAll<IContentDialogService>();
        services.AddSingleton<IContentDialogService, ScriptedContentDialogService>();

        // A page asking for the repository window gets a record of having asked, not a window left
        // open on the headless platform after the test.
        services.RemoveAll<IAppWindows>();
        services.AddSingleton<IAppWindows, RecordingAppWindows>();

        // Nor may a test start another instance of the application.
        services.RemoveAll<IInstanceLauncher>();
        services.AddSingleton<IInstanceLauncher, RecordingInstanceLauncher>();

        // A clock that only moves when a test says so: the automatic refresh must not fire in the
        // middle of a test that is about something else.
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider, ManualTimeProvider>();

        // The clipboard and the file manager belong to whoever is running the tests.
        services.RemoveAll<ISystemInterop>();
        services.AddSingleton<ISystemInterop, RecordingSystemInterop>();

        // So does the folder picker: a test says which folder it answers with, and reads where it
        // was asked to start.
        services.RemoveAll<IFolderDialogService>();
        services.AddSingleton<IFolderDialogService, RecordingFolderDialogService>();

        // And so does their git identity: the real service writes their own global configuration.
        services.RemoveAll<IGitIdentityService>();
        services.AddSingleton<IGitIdentityService, FakeGitIdentityService>();

        configure?.Invoke(services);

        return new TestServices(services.BuildServiceProvider(), root);
    }

    /// <summary>
    /// Gets the recording info-bar service, for asserting what a page reported.
    /// </summary>
    public RecordingInfoBarService InfoBar => (RecordingInfoBarService)Get<IInfoBarService>();

    /// <summary>
    /// Gets the recording overlay service, for asserting an operation showed and hid its progress.
    /// </summary>
    public RecordingOverlayService Overlay => (RecordingOverlayService)Get<IOverlayService>();

    /// <summary>
    /// Gets the recording desktop interop, for asserting what a row menu asked for.
    /// </summary>
    public RecordingSystemInterop Interop => (RecordingSystemInterop)Get<ISystemInterop>();

    /// <summary>
    /// Gets the recording window coordinator, for asserting which window a page asked for.
    /// </summary>
    public RecordingAppWindows Windows => (RecordingAppWindows)Get<IAppWindows>();

    /// <summary>
    /// Gets the recording launcher, for asserting which new instance a page asked for.
    /// </summary>
    public RecordingInstanceLauncher Launcher => (RecordingInstanceLauncher)Get<IInstanceLauncher>();

    /// <summary>
    /// Gets the manual clock the automatic refresh runs on.
    /// </summary>
    public ManualTimeProvider Clock => (ManualTimeProvider)Get<TimeProvider>();

    /// <summary>
    /// Gets the in-memory git identity, for setting what git has and asserting what was written.
    /// </summary>
    public FakeGitIdentityService Identity => (FakeGitIdentityService)Get<IGitIdentityService>();

    /// <summary>
    /// Gets the scripted dialog service, for choosing what a dialog answers.
    /// </summary>
    public ScriptedContentDialogService Dialogs => (ScriptedContentDialogService)Get<IContentDialogService>();

    /// <summary>
    /// Gets the recording folder picker, for choosing the folder it answers with and reading where it
    /// was asked to start.
    /// </summary>
    public RecordingFolderDialogService Folders => (RecordingFolderDialogService)Get<IFolderDialogService>();

    /// <summary>
    /// Orders the branches and tags dialogs by name, A to Z, instead of newest first.
    /// </summary>
    /// <remarks>
    /// For a test about something other than the order that looks a line up among the ones a small
    /// window has realised: the commits a test makes land in the same second or not, so newest first
    /// is an order that changes from one run to the next.
    /// </remarks>
    public void OrderListsByName()
        => Get<ISettingsService>().Update(current => current with
        {
            BranchSortKey = RefSortKey.Name,
            BranchSortDirection = SortDirection.Ascending,
            TagSortKey = RefSortKey.Name,
            TagSortDirection = SortDirection.Ascending,
        });

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();

        if (Directory.Exists(ConfigurationRoot))
        {
            Directory.Delete(ConfigurationRoot, recursive: true);
        }
    }
}
