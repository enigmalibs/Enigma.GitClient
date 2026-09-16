using System;
using System.IO;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Refs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

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

        // The clipboard and the file manager belong to whoever is running the tests.
        services.RemoveAll<ISystemInterop>();
        services.AddSingleton<ISystemInterop, RecordingSystemInterop>();

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
    /// Gets the scripted dialog service, for choosing what a dialog answers.
    /// </summary>
    public ScriptedContentDialogService Dialogs => (ScriptedContentDialogService)Get<IContentDialogService>();

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
