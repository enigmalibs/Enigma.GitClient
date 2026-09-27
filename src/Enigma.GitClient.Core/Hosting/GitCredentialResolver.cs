using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// Works out the logins a git invocation signs in with, from the profile it runs under.
/// </summary>
public interface IGitCredentialResolver
{
    /// <summary>
    /// Works out the logins for a network operation in a repository.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// A login for each HTTP(S) origin the repository's remotes use that an integration of its profile
    /// owns; <see cref="GitCredentials.None"/> when there is none, and git keeps its own.
    /// </returns>
    Task<GitCredentials> ForRepositoryAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IGitCredentialResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// The profile is the one the repository commits as — the same rule that decides whether it may push
/// (<see cref="ProfilePushRule"/>) — and <see cref="ProfileCredentialRule"/> decides which of its
/// integrations answer for which origin.
/// </para>
/// <para>
/// It fails open, unlike the push guard: this is a login, not a permission. When what decides cannot be
/// read, git keeps its own credential helper or SSH key, and a warning says why the token was not used.
/// Without any profile nothing is read beyond the profiles file.
/// </para>
/// </remarks>
public sealed class GitCredentialResolver : IGitCredentialResolver
{
    private readonly IIdentityProfileStore _profiles;
    private readonly IGitIdentityService _identity;
    private readonly IRemoteReader _remotes;
    private readonly IHostAccountService _accounts;
    private readonly IHostProviderRegistry _providers;
    private readonly ILogger<GitCredentialResolver> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="profiles">Keeps the profiles.</param>
    /// <param name="identity">Reads the identity the repository commits with.</param>
    /// <param name="remotes">Reads where the remotes are.</param>
    /// <param name="accounts">Keeps the connected accounts and their tokens.</param>
    /// <param name="providers">Knows which user name each host pairs with a token.</param>
    /// <param name="logger">Receives what signed in, and what could not.</param>
    public GitCredentialResolver(
        IIdentityProfileStore profiles,
        IGitIdentityService identity,
        IRemoteReader remotes,
        IHostAccountService accounts,
        IHostProviderRegistry providers,
        ILogger<GitCredentialResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        _profiles = profiles;
        _identity = identity;
        _remotes = remotes;
        _accounts = accounts;
        _providers = providers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GitCredentials> ForRepositoryAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        try
        {
            IReadOnlyList<IdentityProfile> profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false);

            if (profiles.Count == 0)
            {
                return GitCredentials.None;
            }

            GitIdentity identity = await _identity.GetEffectiveAsync(repository, cancellationToken).ConfigureAwait(false);

            if (IdentityProfile.FirstMatching(profiles, identity) is not { } profile)
            {
                return GitCredentials.None;
            }

            IReadOnlyList<GitRemote> remotes = await _remotes.GetRemotesAsync(repository, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<HostAccount> accounts = await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false);

            List<string> urls = [];

            foreach (GitRemote remote in remotes)
            {
                urls.Add(remote.FetchUrl);
                urls.Add(remote.PushUrl);
            }

            return await BuildAsync(ProfileCredentialRule.Select(profile, accounts, urls), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or GitCommandException or GitNotFoundException
                                              or TokenProtectionException)
        {
            _logger.LogWarning(exception, "The integration git would sign in with could not be read; git keeps its own credentials");

            return GitCredentials.None;
        }
    }

    private async Task<GitCredentials> BuildAsync(IReadOnlyList<HostSignIn> signIns, CancellationToken cancellationToken)
    {
        if (signIns.Count == 0)
        {
            return GitCredentials.None;
        }

        List<GitHostCredential> credentials = [];

        foreach (HostSignIn signIn in signIns)
        {
            SecretString? token = await _accounts.GetTokenAsync(signIn.Account, cancellationToken).ConfigureAwait(false);
            IRepositoryHostProvider? provider = _providers.Find(signIn.Account.Kind);

            if (token is null || token.IsEmpty || provider is null)
            {
                _logger.LogWarning(
                    "Integration {Account} has no token git can use for {Origin}",
                    signIn.Account.Id,
                    signIn.Origin);

                continue;
            }

            if (!GitHostCredential.TryCreate(new Uri(signIn.Origin), provider.GitUserName, token, out GitHostCredential? credential)
                || credential is null)
            {
                _logger.LogWarning(
                    "The token of integration {Account} cannot be handed to git for {Origin}",
                    signIn.Account.Id,
                    signIn.Origin);

                continue;
            }

            _logger.LogDebug("git signs in to {Origin} with integration {Account}", signIn.Origin, signIn.Account.Id);
            credentials.Add(credential);
        }

        return GitCredentials.For(credentials);
    }
}
