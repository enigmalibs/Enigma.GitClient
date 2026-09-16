using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Opens what the user is looking at on the host it came from.
/// </summary>
/// <remarks>
/// The link is always derived from the repository's own remote, never from anything remembered, so a
/// fork, a rename or a transfer resolves to wherever the remote now points. No account is needed —
/// a public repository has a public web page — which is why "Open on GitHub" works before anyone has
/// connected anything.
/// </remarks>
public interface IHostLinkService
{
    /// <summary>
    /// Gets the name of the host the open repository is on, or <see langword="null"/> when it is on
    /// none this client recognises. Menus bind their label and their visibility to it.
    /// </summary>
    string? HostName { get; }

    /// <summary>
    /// Re-reads which host the open repository is on.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once <see cref="HostName"/> is up to date.</returns>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a commit's page.
    /// </summary>
    /// <param name="sha">The commit.</param>
    /// <returns><see langword="true"/> when a browser was launched.</returns>
    Task<bool> OpenCommitAsync(string sha);

    /// <summary>
    /// Opens a branch's page.
    /// </summary>
    /// <param name="branch">The branch's short name.</param>
    /// <returns><see langword="true"/> when a browser was launched.</returns>
    Task<bool> OpenBranchAsync(string branch);

    /// <summary>
    /// Opens a file's page.
    /// </summary>
    /// <param name="reference">The commit or branch to show it at.</param>
    /// <param name="path">The file's path in the repository.</param>
    /// <param name="line">A line to point at, if any.</param>
    /// <returns><see langword="true"/> when a browser was launched.</returns>
    Task<bool> OpenFileAsync(string reference, string path, int? line = null);

    /// <summary>
    /// Opens a repository's own page on its host.
    /// </summary>
    /// <param name="url">The repository's web address, as the host gave it.</param>
    /// <returns><see langword="true"/> when a browser was launched.</returns>
    Task<bool> OpenAsync(string url);
}

/// <summary>
/// Default <see cref="IHostLinkService"/>.
/// </summary>
public sealed class HostLinkService : IHostLinkService
{
    private readonly IRepositoryContext _context;
    private readonly IRemoteReader _remotes;
    private readonly IHostProviderRegistry _registry;
    private readonly IHostAccountService _accounts;
    private readonly ISystemInterop _interop;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<HostLinkService> _logger;

    private HostMatch? _match;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="remotes">Reads the repository's remotes.</param>
    /// <param name="registry">Works out which host a remote is on.</param>
    /// <param name="accounts">Supplies the connected accounts, which is how a self-hosted instance is recognised.</param>
    /// <param name="interop">Hands the address to the desktop.</param>
    /// <param name="infoBar">Reports a link that could not be opened.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public HostLinkService(
        IRepositoryContext context,
        IRemoteReader remotes,
        IHostProviderRegistry registry,
        IHostAccountService accounts,
        ISystemInterop interop,
        IInfoBarService infoBar,
        ILogger<HostLinkService> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _remotes = remotes;
        _registry = registry;
        _accounts = accounts;
        _interop = interop;
        _infoBar = infoBar;
        _logger = logger;

        _context.RepositoryChanged += (_, _) => _ = RefreshAsync();
    }

    /// <inheritdoc />
    public string? HostName => _match?.Provider.DisplayName;

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _match = null;

        RepositoryHandle? repository = _context.Repository;

        if (repository is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<GitRemote> remotes = await _remotes
                .GetRemotesAsync(repository, cancellationToken)
                .ConfigureAwait(true);

            IReadOnlyList<HostAccount> accounts = await _accounts
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(true);

            // origin first, because it is the one a user means by "the" remote; anything else is a
            // fallback for a repository whose remote is called something sensible instead.
            foreach (GitRemote remote in Ordered(remotes))
            {
                HostMatch? match = _registry.Match(remote.FetchUrl, accounts);

                if (match is not null)
                {
                    _match = match;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes under the read.
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "Reading the remotes to find the host failed");
        }
    }

    /// <inheritdoc />
    public Task<bool> OpenCommitAsync(string sha)
        => OpenBuiltAsync(match => match.Provider.BuildCommitUrl(match.Remote, sha));

    /// <inheritdoc />
    public Task<bool> OpenBranchAsync(string branch)
        => OpenBuiltAsync(match => match.Provider.BuildBranchUrl(match.Remote, branch));

    /// <inheritdoc />
    public Task<bool> OpenFileAsync(string reference, string path, int? line = null)
        => OpenBuiltAsync(match => match.Provider.BuildFileUrl(match.Remote, reference, path, line));

    /// <inheritdoc />
    public async Task<bool> OpenAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (await _interop.OpenUrlAsync(url).ConfigureAwait(true))
        {
            return true;
        }

        await ReportAsync("Could not open the link", url).ConfigureAwait(true);

        return false;
    }

    private async Task<bool> OpenBuiltAsync(Func<HostMatch, string?> build)
    {
        if (_match is null)
        {
            await RefreshAsync().ConfigureAwait(true);
        }

        if (_match is null)
        {
            await ReportAsync(
                "No host to open this on",
                "This repository's remote is not on a host this client recognises.").ConfigureAwait(true);

            return false;
        }

        string? url = build(_match);

        return url is { Length: > 0 } && await OpenAsync(url).ConfigureAwait(true);
    }

    private static IEnumerable<GitRemote> Ordered(IReadOnlyList<GitRemote> remotes)
    {
        foreach (GitRemote remote in remotes)
        {
            if (string.Equals(remote.Name, GitRemote.DefaultName, StringComparison.Ordinal))
            {
                yield return remote;
            }
        }

        foreach (GitRemote remote in remotes)
        {
            if (!string.Equals(remote.Name, GitRemote.DefaultName, StringComparison.Ordinal))
            {
                yield return remote;
            }
        }
    }

    private Task ReportAsync(string title, string message)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = InfoBarSeverity.Warning;
        });
}
