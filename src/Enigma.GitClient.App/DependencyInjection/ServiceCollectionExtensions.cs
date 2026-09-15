using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Enigma.GitClient.App.DependencyInjection;

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
            services.AddSingleton<IContentDialogService, ContentDialogService>();
            services.AddSingleton<IOverlayService, OverlayService>();
            services.AddSingleton<IInfoBarService, InfoBarService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IFolderDialogService, FolderDialogService>();

            return services;
        }

        /// <summary>
        /// Registers the window, the pages and their ViewModels.
        /// </summary>
        /// <remarks>
        /// Views are transient and ViewModels are singletons on purpose: navigating back to a page
        /// builds a fresh control but re-attaches the ViewModel it had, so the page keeps its state
        /// while the visual tree does not leak.
        /// </remarks>
        /// <returns>The same collection, so calls can be chained.</returns>
        public IServiceCollection AddGitClientApp()
        {
            services.AddSingleton<IRepositoryContext, RepositoryContext>();

            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            services.AddTransient<HistoryPageView>();
            services.AddTransient<ChangesPageView>();
            services.AddTransient<BranchesPageView>();
            services.AddTransient<RemotesPageView>();
            services.AddTransient<IntegrationsPageView>();
            services.AddTransient<SettingsPageView>();

            services.AddSingleton<HistoryPageViewModel>();
            services.AddSingleton<ChangesPageViewModel>();
            services.AddSingleton<BranchesPageViewModel>();
            services.AddSingleton<RemotesPageViewModel>();
            services.AddSingleton<IntegrationsPageViewModel>();
            services.AddSingleton<SettingsPageViewModel>();

            return services;
        }
    }
}
