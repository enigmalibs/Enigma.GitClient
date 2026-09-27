using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// One integration git signs in with, and the origin it answers for.
/// </summary>
/// <param name="Origin">The origin, such as <c>https://github.com</c>.</param>
/// <param name="Account">The integration whose token signs in.</param>
public sealed record HostSignIn(string Origin, HostAccount Account);

/// <summary>
/// Decides which of a profile's integrations git signs in with, for the addresses it is about to reach.
/// </summary>
/// <remarks>
/// <para>
/// An address gets a login when it is HTTP(S) and an integration of the profile owns its host
/// (<see cref="HostAccount.Owns"/>). SSH addresses never do — a token cannot sign an SSH connection in,
/// and git keeps using the SSH key. The login is keyed by the address's own origin, because that is
/// what git's credential context carries; the first integration found for an origin answers for it.
/// </para>
/// <para>
/// A token is never sent in the clear on behalf of an <c>https</c> integration: an <c>http</c> address
/// gets a login only from an integration whose instance is itself <c>http</c>, which already receives
/// the token that way for its API.
/// </para>
/// </remarks>
public static class ProfileCredentialRule
{
    /// <summary>
    /// Decides the logins for a repository's remote addresses.
    /// </summary>
    /// <param name="profile">The profile the repository commits as.</param>
    /// <param name="accounts">Every connected account.</param>
    /// <param name="remoteUrls">The fetch and push addresses of the repository's remotes.</param>
    /// <returns>One login per origin, in the order the addresses were given.</returns>
    public static IReadOnlyList<HostSignIn> Select(
        IdentityProfile profile,
        IReadOnlyList<HostAccount> accounts,
        IEnumerable<string> remoteUrls)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(remoteUrls);

        List<HostSignIn> signIns = [];
        HashSet<string> origins = new(StringComparer.Ordinal);

        foreach (string url in remoteUrls)
        {
            if (ForProfile(profile, accounts, url) is { } signIn && origins.Add(signIn.Origin))
            {
                signIns.Add(signIn);
            }
        }

        return signIns;
    }

    /// <summary>
    /// Finds the integration of a profile that signs in to one address.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="accounts">Every connected account.</param>
    /// <param name="url">The address.</param>
    /// <returns>The login, or <see langword="null"/> when none of the profile's integrations can give one.</returns>
    public static HostSignIn? ForProfile(IdentityProfile profile, IReadOnlyList<HostAccount> accounts, string? url)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(accounts);

        foreach (HostAccount account in accounts)
        {
            if (account.BelongsTo(profile.Id) && ForAccount(account, url) is { } signIn)
            {
                return signIn;
            }
        }

        return null;
    }

    /// <summary>
    /// Answers whether one integration signs in to one address.
    /// </summary>
    /// <param name="account">The integration.</param>
    /// <param name="url">The address.</param>
    /// <returns>The login, or <see langword="null"/> when the address is not HTTP(S) on its instance.</returns>
    public static HostSignIn? ForAccount(HostAccount account, string? url)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? address)
            || !GitHostCredential.TryGetOrigin(address, out string? origin)
            || origin is null
            || !RemoteUrl.TryParse(url, out RemoteUrl? remote)
            || remote is null
            || !account.Owns(remote))
        {
            return null;
        }

        // Never downgrade: an https instance's token does not travel to an http address.
        if (address.Scheme == Uri.UriSchemeHttp && account.BaseUri.Scheme != Uri.UriSchemeHttp)
        {
            return null;
        }

        return new HostSignIn(origin, account);
    }
}
