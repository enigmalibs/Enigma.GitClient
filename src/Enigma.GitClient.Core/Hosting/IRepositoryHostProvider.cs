using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// One repository host, as far as this client cares about it: who a token belongs to, which
/// repositories it can reach, and where a commit, a branch or a file lives in a browser.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. Issues and pull requests are excluded from this product, so a provider needs
/// exactly the identity call, the repository listing and three URL builders — which is why adding a
/// fourth host is a class rather than a redesign.
/// </para>
/// <para>
/// The token is passed in rather than read by the provider, so a provider never touches the token
/// store and a test never needs one.
/// </para>
/// </remarks>
public interface IRepositoryHostProvider
{
    /// <summary>Gets which host this provider speaks to.</summary>
    HostKind Kind { get; }

    /// <summary>Gets what to call the host in the interface.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the instance root a new account starts with, such as <c>https://github.com</c>.
    /// </summary>
    Uri DefaultBaseUri { get; }

    /// <summary>
    /// Gets the sentence telling the user which token scopes to grant, which every host words
    /// differently and every user gets wrong once.
    /// </summary>
    string TokenScopeHint { get; }

    /// <summary>
    /// Answers whether a remote address is one this provider handles, judged on the address alone.
    /// </summary>
    /// <param name="remote">The parsed remote address.</param>
    /// <returns><see langword="true"/> when the address is on this host.</returns>
    /// <remarks>
    /// Only the public instances can be recognised this way. A self-hosted instance is recognised
    /// through the account the user added for it, which the registry checks first.
    /// </remarks>
    bool MatchesRemote(RemoteUrl remote);

    /// <summary>
    /// Asks the host who a token belongs to, which is how a token is validated.
    /// </summary>
    /// <param name="account">The account the token is for.</param>
    /// <param name="token">The token.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Who the host says it is.</returns>
    /// <exception cref="HostAuthenticationException">The host rejected the token.</exception>
    Task<HostIdentity> ValidateCredentialAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of the repositories an account can reach.
    /// </summary>
    /// <param name="account">The account to list for.</param>
    /// <param name="token">The token.</param>
    /// <param name="query">What to list, and where to carry on from.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page.</returns>
    Task<HostRepositoryPage> ListRepositoriesAsync(
        HostAccount account,
        SecretString token,
        HostRepositoryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the browser address of a commit.
    /// </summary>
    /// <param name="remote">The repository's remote address.</param>
    /// <param name="sha">The commit.</param>
    /// <returns>The address, or <see langword="null"/> when the remote does not name a repository.</returns>
    string? BuildCommitUrl(RemoteUrl remote, string sha);

    /// <summary>
    /// Builds the browser address of a branch.
    /// </summary>
    /// <param name="remote">The repository's remote address.</param>
    /// <param name="branch">The branch's short name.</param>
    /// <returns>The address, or <see langword="null"/> when the remote does not name a repository.</returns>
    string? BuildBranchUrl(RemoteUrl remote, string branch);

    /// <summary>
    /// Builds the browser address of a file.
    /// </summary>
    /// <param name="remote">The repository's remote address.</param>
    /// <param name="reference">The commit or branch to show the file at.</param>
    /// <param name="path">The file's path in the repository.</param>
    /// <param name="line">A line to point at, if any.</param>
    /// <returns>The address, or <see langword="null"/> when the remote does not name a repository.</returns>
    string? BuildFileUrl(RemoteUrl remote, string reference, string path, int? line = null);
}

/// <summary>
/// What a remote address turned out to be: which host handles it, and which account can speak to it.
/// </summary>
/// <param name="Provider">The provider for the host.</param>
/// <param name="Remote">The parsed address.</param>
/// <param name="Account">
/// The account whose instance the address is on, or <see langword="null"/> when the host is
/// recognised but no account has been added for it — enough for a deep link, not enough for an API
/// call.
/// </param>
public sealed record HostMatch(IRepositoryHostProvider Provider, RemoteUrl Remote, HostAccount? Account = null)
{
    /// <summary>Gets a value indicating whether there is an account to make API calls with.</summary>
    public bool HasAccount => Account is not null;
}

/// <summary>
/// Finds the provider for a host.
/// </summary>
public interface IHostProviderRegistry
{
    /// <summary>Gets every provider that has been registered.</summary>
    IReadOnlyList<IRepositoryHostProvider> Providers { get; }

    /// <summary>
    /// Finds the provider for a kind of host.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The provider, or <see langword="null"/> when none is registered for it.</returns>
    IRepositoryHostProvider? Find(HostKind kind);

    /// <summary>
    /// Works out which host — and which account — a remote address belongs to.
    /// </summary>
    /// <param name="remoteUrl">The remote address, in any form git accepts.</param>
    /// <param name="accounts">The accounts the user has added.</param>
    /// <returns>The match, or <see langword="null"/> when the address is not on a known host.</returns>
    HostMatch? Match(string? remoteUrl, IEnumerable<HostAccount>? accounts = null);
}

/// <summary>
/// Default <see cref="IHostProviderRegistry"/>.
/// </summary>
/// <remarks>
/// Accounts are consulted before the table of public host names, because that is the only way a
/// self-hosted instance can be recognised: <c>git.example.com</c> says nothing about itself, and the
/// account the user added for it says everything.
/// </remarks>
public sealed class HostProviderRegistry : IHostProviderRegistry
{
    private readonly List<IRepositoryHostProvider> _providers;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="providers">The providers to resolve against.</param>
    public HostProviderRegistry(IEnumerable<IRepositoryHostProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = [.. providers];
    }

    /// <inheritdoc />
    public IReadOnlyList<IRepositoryHostProvider> Providers => _providers;

    /// <inheritdoc />
    public IRepositoryHostProvider? Find(HostKind kind)
    {
        foreach (IRepositoryHostProvider provider in _providers)
        {
            if (provider.Kind == kind)
            {
                return provider;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public HostMatch? Match(string? remoteUrl, IEnumerable<HostAccount>? accounts = null)
    {
        if (!RemoteUrl.TryParse(remoteUrl, out RemoteUrl? remote) || remote is null)
        {
            return null;
        }

        if (accounts is not null)
        {
            foreach (HostAccount account in accounts)
            {
                if (!account.Owns(remote))
                {
                    continue;
                }

                IRepositoryHostProvider? owner = Find(account.Kind);

                if (owner is not null)
                {
                    return new HostMatch(owner, remote, account);
                }
            }
        }

        HostKind kind = WellKnownHosts.Detect(remote);
        IRepositoryHostProvider? provider = kind == HostKind.Unknown ? null : Find(kind);

        provider ??= FindByRemote(remote);

        // No account: the host is recognised, which is enough to build a deep link and not enough
        // to call its API. An account of the same kind on a *different* instance is deliberately
        // not attached — a GitHub Enterprise token is not a github.com token.
        return provider is null ? null : new HostMatch(provider, remote);
    }

    private IRepositoryHostProvider? FindByRemote(RemoteUrl remote)
    {
        foreach (IRepositoryHostProvider provider in _providers)
        {
            if (provider.MatchesRemote(remote))
            {
                return provider;
            }
        }

        return null;
    }
}
