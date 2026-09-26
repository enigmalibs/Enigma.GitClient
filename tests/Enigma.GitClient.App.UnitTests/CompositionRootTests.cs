using System;
using System.Collections.Generic;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Guards the composition root. A missing registration or a captive dependency is invisible until
/// the page that needs it is opened, which is exactly the kind of failure a build should catch.
/// </summary>
public sealed class CompositionRootTests
{
    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        App.ConfigureServices(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            // Proves every registration can have a constructor selected, and that no singleton
            // depends on something shorter-lived than itself.
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public void Container_ValidatesOnBuild()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData(typeof(IGitExecutable))]
    [InlineData(typeof(IGitCommandFactory))]
    [InlineData(typeof(IGitProcessRunner))]
    [InlineData(typeof(IGitEnvironment))]
    [InlineData(typeof(IRepositoryLocator))]
    [InlineData(typeof(ICommitLogReader))]
    [InlineData(typeof(IRefReader))]
    [InlineData(typeof(IRemoteReader))]
    public void Container_ResolvesEveryCoreService(Type serviceType)
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService(serviceType));
    }

    [Theory]
    [InlineData(typeof(INavigationService))]
    [InlineData(typeof(IContentDialogService))]
    [InlineData(typeof(IOverlayService))]
    [InlineData(typeof(IInfoBarService))]
    [InlineData(typeof(IFileDialogService))]
    [InlineData(typeof(IFolderDialogService))]
    public void Container_ResolvesEveryEnigmaDesktopService(Type serviceType)
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService(serviceType));
    }

    [Theory]
    [InlineData(typeof(IBranchOperations))]
    [InlineData(typeof(ITagOperations))]
    [InlineData(typeof(ICheckoutOperations))]
    [InlineData(typeof(ISyncOperations))]
    [InlineData(typeof(IMergeOperations))]
    [InlineData(typeof(IBranchDropOperations))]
    public void Container_ResolvesEveryOperationsService(Type serviceType)
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService(serviceType));
    }

    [Theory]
    [InlineData(typeof(HistoryPageViewModel))]
    [InlineData(typeof(ChangesPageViewModel))]
    [InlineData(typeof(BranchesPageViewModel))]
    [InlineData(typeof(TagsPageViewModel))]
    [InlineData(typeof(RemotesPageViewModel))]
    [InlineData(typeof(IdentityPageViewModel))]
    [InlineData(typeof(IntegrationsPageViewModel))]
    [InlineData(typeof(SettingsPageViewModel))]
    public void Container_ResolvesEveryPageViewModel(Type viewModelType)
    {
        using ServiceProvider provider = BuildProvider();

        object viewModel = provider.GetRequiredService(viewModelType);

        Assert.IsAssignableFrom<PageViewModelBase>(viewModel);
    }

    [Fact]
    public void PageViewModels_AreSingletonsSoAPageKeepsItsStateAcrossNavigation()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<HistoryPageViewModel>(),
            provider.GetRequiredService<HistoryPageViewModel>());
    }

    [Fact]
    public void RepositoryContext_IsASingletonSoEveryPageSeesTheSameRepository()
    {
        using ServiceProvider provider = BuildProvider();

        IRepositoryContext first = provider.GetRequiredService<IRepositoryContext>();
        IRepositoryContext second = provider.GetRequiredService<IRepositoryContext>();

        Assert.Same(first, second);
    }

    [Fact]
    public void EveryPageViewModel_ObservesTheOneRepositoryContext()
    {
        using ServiceProvider provider = BuildProvider();

        IRepositoryContext context = provider.GetRequiredService<IRepositoryContext>();

        List<PageViewModelBase> pages =
        [
            provider.GetRequiredService<HistoryPageViewModel>(),
            provider.GetRequiredService<ChangesPageViewModel>(),
            provider.GetRequiredService<BranchesPageViewModel>(),
            provider.GetRequiredService<TagsPageViewModel>(),
            provider.GetRequiredService<RemotesPageViewModel>(),
            provider.GetRequiredService<IdentityPageViewModel>(),
            provider.GetRequiredService<IntegrationsPageViewModel>(),
            provider.GetRequiredService<SettingsPageViewModel>(),
        ];

        Assert.All(pages, page => Assert.Same(context, page.RepositoryContext));
    }

    [Fact]
    public void BuildHost_ProducesAHostWhoseServicesResolve()
    {
        using Microsoft.Extensions.Hosting.IHost host = App.BuildHost();

        Assert.NotNull(host.Services.GetRequiredService<IRepositoryContext>());
        Assert.NotNull(host.Services.GetRequiredService<IGitEnvironment>());
    }
}
