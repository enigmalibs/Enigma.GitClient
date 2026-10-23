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
/// An integration's stored token could not be read, so git signs in on its own where it would have
/// used it.
/// </summary>
public sealed class TokenUnreadableEventArgs : EventArgs
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="account">The integration whose token could not be read.</param>
    /// <param name="origin">The origin git would have signed in to with it, such as <c>https://github.com</c>.</param>
    /// <param name="reason">Why the token could not be read.</param>
    public TokenUnreadableEventArgs(HostAccount account, string origin, TokenProtectionReason reason)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        Account = account;
        Origin = origin;
        Reason = reason;
    }

    /// <summary>Gets the integration whose token could not be read.</summary>
    public HostAccount Account { get; }

    /// <summary>Gets the origin git would have signed in to with it.</summary>
    public string Origin { get; }

    /// <summary>Gets why the token could not be read.</summary>
    public TokenProtectionReason Reason { get; }
}

/// <summary>
/// Works out the logins a git invocation signs in with, from the profile it runs under.
/// </summary>
public interface IGitCredentialResolver
{
    /// <summary>
    /// Raised when an integration's stored token could not be read and git falls back to its own
    /// sign-in for its origin — what the user is told, since git's own prompt says nothing of why.
    /// </summary>
    /// <remarks>Raised on whatever thread the resolution runs on.</remarks>
    event EventHandler<TokenUnreadableEventArgs>? TokenUnreadable;

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

    /// <summary>
    /// Works out the login for a clone, which has no repository — and so no identity of its own — yet.
    /// </summary>
    /// <param name="url">The address cloned from.</param>
    /// <param name="account">
    /// The integration the repository was picked from, or <see langword="null"/> for the current
    /// profile's — the one matching the global identity — that owns the address.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The login, or <see cref="GitCredentials.None"/> when the address gets none.</returns>
    Task<GitCredentials> ForCloneAsync(string url, HostAccount? account, CancellationToken cancellationToken = default);
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
/// <para>
/// Each integration's token is read on its own: one that cannot be read leaves git its own sign-in for
/// that origin only, and <see cref="TokenUnreadable"/> says so — the other integrations still sign in
/// (BUG-3024).
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
    public event EventHandler<TokenUnreadableEventArgs>? TokenUnreadable;

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

    /// <inheritdoc />
    public async Task<GitCredentials> ForCloneAsync(
        string url,
        HostAccount? account,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        try
        {
            if (account is not null)
            {
                // Picked from that integration's own listing: its token is the one the user chose.
                return await BuildAsync(ToList(ProfileCredentialRule.ForAccount(account, url)), cancellationToken)
                    .ConfigureAwait(false);
            }

            IReadOnlyList<IdentityProfile> profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false);

            if (profiles.Count == 0)
            {
                return GitCredentials.None;
            }

            GitIdentity identity = await _identity.GetGlobalAsync(cancellationToken).ConfigureAwait(false);

            if (IdentityProfile.FirstMatching(profiles, identity) is not { } profile)
            {
                return GitCredentials.None;
            }

            IReadOnlyList<HostAccount> accounts = await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false);

            return await BuildAsync(ToList(ProfileCredentialRule.ForProfile(profile, accounts, url)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or GitCommandException or GitNotFoundException
                                              or TokenProtectionException)
        {
            _logger.LogWarning(exception, "The integration a clone would sign in with could not be read; git keeps its own credentials");

            return GitCredentials.None;
        }
    }

    private static IReadOnlyList<HostSignIn> ToList(HostSignIn? signIn) => signIn is null ? [] : [signIn];

    private async Task<GitCredentials> BuildAsync(IReadOnlyList<HostSignIn> signIns, CancellationToken cancellationToken)
    {
        if (signIns.Count == 0)
        {
            return GitCredentials.None;
        }

        List<GitHostCredential> credentials = [];

        foreach (HostSignIn signIn in signIns)
        {
            SecretString? token;

            try
            {
                token = await _accounts.GetTokenAsync(signIn.Account, cancellationToken).ConfigureAwait(false);
            }
            catch (TokenProtectionException exception)
            {
                // Git signs in on its own here, as it would with no integration; the user is told why
                // rather than meeting git's own prompt with no explanation.
                _logger.LogWarning(
                    exception,
                    "The token of integration {Account} could not be read ({Reason}); git signs in to {Origin} on its own",
                    signIn.Account.Id,
                    exception.Reason,
                    signIn.Origin);

                TokenUnreadable?.Invoke(this, new TokenUnreadableEventArgs(signIn.Account, signIn.Origin, exception.Reason));

                continue;
            }

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
