using System;
using System.Collections.Generic;
using System.IO;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.DependencyInjection;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Never the developer's own configuration directory: every transfer now reads the profiles
        // file, and a test must not depend on — or change — the machine it runs on.
        services.RemoveAll<IAppPaths>();
        services.AddSingleton<IAppPaths>(new AppPaths(Path.Combine(workspace.RootPath, "config")));

        services.Configure<GitExecutableOptions>(options =>
        {
            ApplyEnvironment(options, workspace);
            configure?.Invoke(options);
        });

        return services;
    }
}
