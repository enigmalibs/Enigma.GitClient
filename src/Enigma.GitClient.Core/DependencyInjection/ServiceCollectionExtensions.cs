using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Checkout;
using Enigma.GitClient.Core.Commits;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Staging;
using Enigma.GitClient.Core.Stashes;
using Enigma.GitClient.Core.Status;
using Enigma.GitClient.Core.Sync;
using Enigma.GitClient.Core.Tags;
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
        /// runner, the environment probe, repository discovery and creation, the commit-log
        /// reader, the reference and remote readers, and the user's configuration paths.
        /// </summary>
        /// <returns>The same collection, so calls can be chained.</returns>
        public IServiceCollection AddGitClientCore()
        {
            services.AddOptions<GitExecutableOptions>();

            services.AddSingleton<IGitExecutable, GitExecutable>();
            services.AddSingleton<IGitCommandFactory, GitCommandFactory>();
            services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
            services.AddSingleton<IGitEnvironment, GitEnvironment>();
            services.AddSingleton<IAppPaths, AppPaths>();
            services.AddSingleton<IRepositoryLocator, RepositoryLocator>();
            services.AddSingleton<IRepositoryService, RepositoryService>();
            services.AddSingleton<ICommitLogReader, CommitLogReader>();
            services.AddSingleton<IRefReader, RefReader>();
            services.AddSingleton<IRemoteReader, RemoteReader>();
            services.AddSingleton<IWorkingTreeProbe, WorkingTreeProbe>();
            services.AddSingleton<IStatusService, StatusService>();
            services.AddSingleton<IGitIgnoreService, GitIgnoreService>();
            services.AddSingleton<IStagingService, StagingService>();
            services.AddSingleton<ICommitService, CommitService>();
            services.AddSingleton<ISyncService, SyncService>();
            services.AddSingleton<IRemoteService, RemoteService>();
            services.AddSingleton<IStashService, StashService>();
            services.AddSingleton<IMergeService, MergeService>();
            services.AddSingleton<IConflictService, ConflictService>();
            services.AddSingleton<IDiffService, DiffService>();
            services.AddSingleton<IBranchService, BranchService>();
            services.AddSingleton<ITagService, TagService>();
            services.AddSingleton<ICheckoutService, CheckoutService>();

            return services;
        }
    }
}
