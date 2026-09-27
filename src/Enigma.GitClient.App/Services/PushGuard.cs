using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Says whether a repository may push to one of its remotes, from the profile it commits as.
/// </summary>
/// <remarks>
/// A profile pushes only where one of its integrations leads, and a profile without integrations
/// never pushes: <see cref="ProfilePushRule"/> is the rule, this service reads what it needs.
/// </remarks>
public interface IPushGuard
{
    /// <summary>
    /// Decides a push.
    /// </summary>
    /// <param name="repository">The repository pushing.</param>
    /// <param name="remote">The name of the remote it pushes to.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>Whether the push may go ahead. A read that failed refuses it.</returns>
    Task<PushPermission> CheckAsync(RepositoryHandle repository, string remote, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IPushGuard"/>.
/// </summary>
/// <remarks>
/// It fails closed: "this profile never pushes" is a promise, and a push nobody could check is a push
/// that might break it. Without any profile at all nothing is read beyond the profiles file, so people
/// who do not use profiles pay nothing for the check.
/// </remarks>
public sealed class PushGuard : IPushGuard
{
    private readonly IIdentityProfileStore _profiles;
    private readonly IHostAccountService _accounts;
    private readonly IGitIdentityService _identity;
    private readonly IRemoteReader _remotes;
    private readonly ILogger<PushGuard> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="profiles">Keeps the profiles.</param>
    /// <param name="accounts">Keeps the connected accounts.</param>
    /// <param name="identity">Reads the identity the repository commits with.</param>
    /// <param name="remotes">Reads where each remote pushes to.</param>
    /// <param name="logger">Receives a refused push, and a check that could not be made.</param>
    public PushGuard(
        IIdentityProfileStore profiles,
        IHostAccountService accounts,
        IGitIdentityService identity,
        IRemoteReader remotes,
        ILogger<PushGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(logger);

        _profiles = profiles;
        _accounts = accounts;
        _identity = identity;
        _remotes = remotes;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PushPermission> CheckAsync(
        RepositoryHandle repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);

        try
        {
            IReadOnlyList<IdentityProfile> profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(true);

            if (profiles.Count == 0)
            {
                return PushPermission.WithoutProfile;
            }

            GitIdentity identity = await _identity.GetEffectiveAsync(repository, cancellationToken).ConfigureAwait(true);
            IReadOnlyList<GitRemote> remotes = await _remotes.GetRemotesAsync(repository, cancellationToken).ConfigureAwait(true);

            GitRemote? target = null;

            foreach (GitRemote candidate in remotes)
            {
                if (string.Equals(candidate.Name, remote, StringComparison.Ordinal))
                {
                    target = candidate;
                    break;
                }
            }

            // A remote that is not there cannot be pushed to at all, and git says so in its own words.
            if (target is null)
            {
                return PushPermission.WithoutProfile;
            }

            IReadOnlyList<HostAccount> accounts = await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(true);

            PushPermission permission = ProfilePushRule.Evaluate(profiles, accounts, identity, target.PushUrl);

            if (!permission.IsAllowed)
            {
                // By id and host: the profile's label and identity are the user's, and stay out of logs.
                _logger.LogInformation(
                    "A push to {Host} was refused: profile {Profile} has no integration for it",
                    permission.Target,
                    permission.Profile?.Id);
            }

            return permission;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or GitCommandException or GitNotFoundException
                                              or TokenProtectionException)
        {
            _logger.LogWarning(exception, "The profile a push goes out under could not be checked");

            return PushPermission.Unchecked();
        }
    }
}
