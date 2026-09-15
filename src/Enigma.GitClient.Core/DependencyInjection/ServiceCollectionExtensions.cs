using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Enigma.GitClient.Core.DependencyInjection;

/// <summary>
/// Registers the Enigma.GitClient engine with a dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the git engine: executable resolution, the command factory, the process
        /// runner, the environment probe, repository discovery and the commit-log reader.
        /// </summary>
        /// <returns>The same collection, so calls can be chained.</returns>
        public IServiceCollection AddGitClientCore()
        {
            services.AddOptions<GitExecutableOptions>();

            services.AddSingleton<IGitExecutable, GitExecutable>();
            services.AddSingleton<IGitCommandFactory, GitCommandFactory>();
            services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
            services.AddSingleton<IGitEnvironment, GitEnvironment>();
            services.AddSingleton<IRepositoryLocator, RepositoryLocator>();
            services.AddSingleton<ICommitLogReader, CommitLogReader>();

            return services;
        }
    }
}
