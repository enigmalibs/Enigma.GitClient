using System;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.DependencyInjection;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// Builds a dependency-injection container holding the real engine services, wired to run every
/// git invocation with a workspace's isolated environment.
/// </summary>
public sealed class CoreTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private CoreTestHost(ServiceProvider provider) => _provider = provider;

    /// <summary>
    /// Gets the container.
    /// </summary>
    public IServiceProvider Services => _provider;

    /// <summary>
    /// Resolves a required service.
    /// </summary>
    /// <typeparam name="TService">The service type.</typeparam>
    /// <returns>The resolved service.</returns>
    public TService GetRequiredService<TService>()
        where TService : notnull
        => _provider.GetRequiredService<TService>();

    /// <summary>
    /// Builds a host whose git invocations run inside the given workspace's isolated environment.
    /// </summary>
    /// <param name="workspace">The workspace supplying the environment.</param>
    /// <param name="configure">An optional extra configuration step for the engine options.</param>
    /// <returns>The host.</returns>
    public static CoreTestHost Create(GitWorkspace workspace, Action<GitExecutableOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return new CoreTestHost(CoreTestHostFactory.BuildServices(workspace, configure).BuildServiceProvider());
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
