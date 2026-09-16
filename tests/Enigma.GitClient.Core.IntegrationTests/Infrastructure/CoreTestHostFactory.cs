using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.DependencyInjection;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// Builds hosts with non-default engine options, for the tests that need to misconfigure the
/// engine on purpose.
/// </summary>
public static class CoreTestHostFactory
{
    /// <summary>
    /// Builds a host pointed at a specific git executable path.
    /// </summary>
    /// <param name="workspace">The workspace supplying the environment.</param>
    /// <param name="executablePath">The executable path to configure.</param>
    /// <returns>The host.</returns>
    public static CoreTestHost WithExecutablePath(GitWorkspace workspace, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return CoreTestHost.Create(workspace, options =>
        {
            options.ExecutablePath = executablePath;
        });
    }

    internal static void ApplyEnvironment(GitExecutableOptions options, GitWorkspace workspace)
    {
        foreach (KeyValuePair<string, string> entry in workspace.Environment)
        {
            options.EnvironmentOverrides[entry.Key] = entry.Value;
        }
    }

    internal static ServiceCollection BuildServices(GitWorkspace workspace, Action<GitExecutableOptions>? configure)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddGitClientCore();
        services.Configure<GitExecutableOptions>(options =>
        {
            ApplyEnvironment(options, workspace);
            configure?.Invoke(options);
        });

        return services;
    }
}
