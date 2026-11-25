using System;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Desktop.Navigation;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.GitClient.Desktop.Views.Panels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Enigma.GitClient.Desktop.DependencyInjection;

/// <summary>
/// Composition root helpers for the desktop application.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the six Enigma.Avalonia.Desktop services. The library ships no registration
        /// extension of its own, so this is it.
        /// </summary>
        /// <returns>The same collection, so calls can be chained.</returns>
        public IServiceCollection AddEnigmaDesktopServices()
        {
            services.AddSingleton<INavigationService, NavigationService>();

            // The start window's rail is a second navigation, with a selection of its own.
            services.AddKeyedSingleton<INavigationService, NavigationService>(StartNavigation.ServiceKey);
            services.AddSingleton<IContentDialogService, ContentDialogService>();
            services.AddSingleton<IOverlayService, OverlayService>();
            services.AddSingleton<IInfoBarService, InfoBarService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IFolderDialogService, FolderDialogService>();

            return services;
        }

        /// <summary>
        /// Registers the windows, the pages and their ViewModels.
        /// </summary>
        /// <remarks>
        /// Views and windows are transient and ViewModels are singletons on purpose: navigating back
        /// to a page builds a fresh control but re-attaches the ViewModel it had, so the page keeps
        /// its state while the visual tree does not leak — and a window, which cannot be shown again
        /// once it has closed, is simply built again the next time it is needed.
        /// </remarks>
        /// <returns>The same collection, so calls can be chained.</returns>
        public IServiceCollection AddGitClientApp()
        {
            services.AddSingleton<IRepositoryContext, RepositoryContext>();
            services.AddSingleton<IRepositoryListStore, RepositoryListStore>();
            services.AddSingleton<IRepositoryIconStore, RepositoryIconStore>();
            services.AddSingleton<IProfileSelection, ProfileSelection>();
            services.AddSingleton<IHiddenBranchStore, HiddenBranchStore>();
            services.AddSingleton<IHiddenBranches, HiddenBranches>();
            services.AddSingleton<IShellNavigation, ShellNavigation>();
            services.AddSingleton<IStartNavigation, StartNavigation>();
            services.AddSingleton<IScreenReader, AvaloniaScreenReader>();
            services.AddSingleton<IWindowPlacer, WindowPlacer>();
            services.AddSingleton<WindowHostBinder>();
            services.AddSingleton<IAppWindows, AppWindows>();
            services.AddSingleton<IRepositoryOpener, RepositoryOpener>();
            services.AddSingleton<IRepositoryCloner, RepositoryCloner>();

            // The launcher reaches the window service only when it launches, through a factory: taking
            // it in its constructor closes a cycle through the operations that launch (BUG-FACC).
            services.AddSingleton<Func<IAppWindows>>(provider => provider.GetRequiredService<IAppWindows>);
            services.AddSingleton<IInstanceLauncher, InstanceLauncher>();
            services.AddSingleton<IToolDialogService, ToolDialogService>();
            services.AddSingleton<IAboutDialogService, AboutDialogService>();
            services.AddSingleton<IThemeSwitcher, ThemeSwitcher>();
            services.AddSingleton<ICommitDetailsDialogService, CommitDetailsDialogService>();

            // The safety net every exception nothing else caught ends up in (BUG-9DE3).
            services.AddSingleton<IUnhandledErrorReporter, UnhandledErrorReporter>();

            // The way out of stored tokens this account can no longer read (BUG-3024).
            services.AddSingleton<ITokenRecovery, TokenRecovery>();

            // The automatic refresh measures its interval on this clock, which a test replaces.
            services.TryAddSingleton(TimeProvider.System);

            // The two background loops are hosted services: the host starts them and stops them, and
            // their callers keep the same instance through the interface (ASYNC-02).
            services.AddSingleton<AutoRefreshService>();
            services.AddSingleton<IAutoRefreshService>(provider => provider.GetRequiredService<AutoRefreshService>());
            services.AddHostedService(provider => provider.GetRequiredService<AutoRefreshService>());
            services.AddSingleton<IRemoteTags, RemoteTags>();

            // The file-system watcher, on top of it: what changes on disk is refreshed within a second.
            services.AddSingleton<IRepositoryEventSource, FileSystemRepositoryEventSource>();
            services.AddSingleton<RepositoryWatcher>();
            services.AddSingleton<IRepositoryWatcher>(provider => provider.GetRequiredService<RepositoryWatcher>());
            services.AddHostedService(provider => provider.GetRequiredService<RepositoryWatcher>());
            services.AddSingleton<ISystemInterop, SystemInterop>();
            services.AddSingleton<IWorktreeGuard, WorktreeGuard>();
            services.AddSingleton<IBranchOperations, BranchOperations>();
            services.AddSingleton<IWorktreeOperations, WorktreeOperations>();
            services.AddSingleton<ISubmoduleOperations, SubmoduleOperations>();
            services.AddSingleton<ISubmoduleFollowStep, SubmoduleFollowStep>();
            services.AddSingleton<ITagOperations, TagOperations>();
            services.AddSingleton<ICheckoutOperations, CheckoutOperations>();
            services.AddSingleton<IResetOperations, ResetOperations>();
            services.AddSingleton<IRevertOperations, RevertOperations>();
            services.AddSingleton<ISquashOperations, SquashOperations>();
            services.AddSingleton<IPushGuard, PushGuard>();
            services.AddSingleton<ISyncOperations, SyncOperations>();
            services.AddSingleton<IMergeOperations, MergeOperations>();
            services.AddSingleton<IBranchDropOperations, BranchDropOperations>();
            services.AddSingleton<IStashOperations, StashOperations>();
            services.AddSingleton<IDiscardOperations, DiscardOperations>();
            services.AddSingleton<IHostLinkService, HostLinkService>();
            services.AddSingleton<IHostRepositoryBrowser, HostRepositoryBrowser>();

            services.AddTransient<StartWindow>();
            services.AddSingleton<StartWindowViewModel>();
            services.AddTransient<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            // Built with the repository window's ViewModel, and kept for the session: what it has said
            // stays said from one repository window to the next.
            services.AddSingleton<GitSignInNoticeViewModel>();
            services.AddSingleton<SubmoduleNoticeViewModel>();
            services.AddSingleton<SubmoduleWindowViewModel>();

            services.AddTransient<RepositoriesPageView>();
            services.AddTransient<HistoryPageView>();
            services.AddTransient<BranchesPageView>();
            services.AddTransient<TagsPageView>();
            services.AddTransient<RemotesPageView>();
            services.AddTransient<WorktreesPageView>();
            services.AddTransient<SubmodulesPageView>();
            services.AddTransient<ConflictResolutionPageView>();
            services.AddTransient<ProfilesPageView>();
            services.AddTransient<SettingsPageView>();

            services.AddSingleton<RepositoriesPageViewModel>();
            services.AddSingleton<HistoryBranchActions>();
            services.AddSingleton<HistoryTagActions>();
            services.AddSingleton<HistoryPageViewModel>();
            services.AddSingleton<BranchesPageViewModel>();
            services.AddSingleton<TagsPageViewModel>();
            services.AddSingleton<RemotesPageViewModel>();
            services.AddSingleton<WorktreesPageViewModel>();
            services.AddSingleton<SubmodulesPageViewModel>();
            services.AddSingleton<ConflictResolutionPageViewModel>();
            services.AddSingleton<ProfilesPageViewModel>();
            services.AddSingleton<SettingsPageViewModel>();

            // The history's details panel for the uncommitted line: one working tree, with its
            // message, for the one history.
            services.AddSingleton<WorkingTreePanelViewModel>();

            // Dialog views are transient: each showing gets a fresh control bound to a fresh
            // ViewModel, so a cancelled dialog never leaves its half-typed state behind.
            services.AddTransient<CloneRepositoryDialogView>();
            services.AddTransient<InitRepositoryDialogView>();
            services.AddTransient<CreateBranchDialogView>();
            services.AddTransient<RenameBranchDialogView>();
            services.AddTransient<SetUpstreamDialogView>();
            services.AddTransient<CreateTagDialogView>();
            services.AddTransient<RemoteDialogView>();
            services.AddTransient<AddHostAccountDialogView>();
            services.AddTransient<HostRepositoriesDialogView>();
            services.AddTransient<IdentityProfileDialogView>();

            // The singletons that show those dialogs build each view through a factory: a fresh one per
            // showing, and a constructor that says which views it shows.
            services.AddSingleton<Func<CloneRepositoryDialogView>>(provider => provider.GetRequiredService<CloneRepositoryDialogView>);
            services.AddSingleton<Func<InitRepositoryDialogView>>(provider => provider.GetRequiredService<InitRepositoryDialogView>);
            services.AddSingleton<Func<AddHostAccountDialogView>>(provider => provider.GetRequiredService<AddHostAccountDialogView>);
            services.AddSingleton<Func<HostRepositoriesDialogView>>(provider => provider.GetRequiredService<HostRepositoriesDialogView>);
            services.AddSingleton<Func<IdentityProfileDialogView>>(provider => provider.GetRequiredService<IdentityProfileDialogView>);

            // The diff viewer is per-consumer: two places showing a diff must not share a scroll
            // position, a view mode or a selection.
            services.AddTransient<DiffViewerViewModel>();
            services.AddTransient<DiffViewerView>();

            // How a singleton gets a viewer of its own: built outside the container's tracking, so the
            // owner — which disposes it — is the only thing holding it (SOLID-04). Resolved from the root
            // provider instead, every viewer would stay referenced by the container until the end.
            services.AddSingleton<Func<DiffViewerViewModel>>(
                provider => () => ActivatorUtilities.CreateInstance<DiffViewerViewModel>(provider));

            return services;
        }
    }
}
