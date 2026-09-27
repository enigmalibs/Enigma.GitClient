using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// Why a push may, or may not, go ahead.
/// </summary>
public enum PushPermissionReason
{
    /// <summary>No profile matches the identity the repository commits with, so no profile decides.</summary>
    NoProfile,

    /// <summary>The repository's profile has an integration for the remote's host.</summary>
    Integration,

    /// <summary>The repository's profile has no integration for the remote's host.</summary>
    NoIntegration,

    /// <summary>What decides could not be read, so nothing is pushed.</summary>
    CheckFailed,
}

/// <summary>
/// Whether a push may go ahead, and on whose behalf.
/// </summary>
public sealed record PushPermission
{
    private PushPermission(PushPermissionReason reason, IdentityProfile? profile, string target, HostAccount? account)
    {
        Reason = reason;
        Profile = profile;
        Target = target;
        Account = account;
    }

    /// <summary>Gets why the push may or may not go ahead.</summary>
    public PushPermissionReason Reason { get; }

    /// <summary>Gets a value indicating whether the push may go ahead.</summary>
    public bool IsAllowed => Reason is PushPermissionReason.NoProfile or PushPermissionReason.Integration;

    /// <summary>Gets the profile that decided, or <see langword="null"/> when none did.</summary>
    public IdentityProfile? Profile { get; }

    /// <summary>
    /// Gets where the push goes: the remote's host, or the address itself when it names no host — a
    /// path on this machine. Empty when nothing was looked at.
    /// </summary>
    public string Target { get; }

    /// <summary>Gets the integration that allowed the push, or <see langword="null"/>.</summary>
    public HostAccount? Account { get; }

    /// <summary>Gets the permission given when no profile decides.</summary>
    public static PushPermission WithoutProfile { get; } = new(PushPermissionReason.NoProfile, null, string.Empty, null);

    /// <summary>
    /// Builds the refusal given when what decides could not be read.
    /// </summary>
    /// <returns>The permission.</returns>
    public static PushPermission Unchecked() => new(PushPermissionReason.CheckFailed, null, string.Empty, null);

    /// <summary>
    /// Builds the permission a profile's integration gives.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="target">Where the push goes.</param>
    /// <param name="account">The integration.</param>
    /// <returns>The permission.</returns>
    public static PushPermission Through(IdentityProfile profile, string target, HostAccount account)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(account);

        return new PushPermission(PushPermissionReason.Integration, profile, target ?? string.Empty, account);
    }

    /// <summary>
    /// Builds the refusal given when a profile has no integration for where the push goes.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="target">Where the push goes.</param>
    /// <returns>The permission.</returns>
    public static PushPermission Refused(IdentityProfile profile, string target)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new PushPermission(PushPermissionReason.NoIntegration, profile, target ?? string.Empty, null);
    }
}

/// <summary>
/// Decides whether a repository may push to a remote, from the profile it commits as.
/// </summary>
/// <remarks>
/// <para>
/// A profile pushes only where one of its integrations leads: the push goes ahead when an account that
/// belongs to the profile owns the remote's host (<see cref="HostAccount.Owns"/>), and not otherwise. A
/// profile without integrations is local only — it never pushes, to a host or to a path on this machine.
/// </para>
/// <para>
/// The profile is the one whose name and email match the identity the repository commits with — the
/// same rule that makes a profile the current one. When none does, no profile decides and the push goes
/// ahead, which is what it did before profiles had integrations.
/// </para>
/// <para>
/// The integration is the permission, not the credential: git keeps authenticating with its own
/// credential helper or SSH key.
/// </para>
/// </remarks>
public static class ProfilePushRule
{
    /// <summary>
    /// Decides a push.
    /// </summary>
    /// <param name="profiles">Every profile, in the order they were added.</param>
    /// <param name="accounts">Every connected account.</param>
    /// <param name="identity">The identity the repository commits with.</param>
    /// <param name="pushUrl">The address the push goes to.</param>
    /// <returns>Whether it may go ahead.</returns>
    public static PushPermission Evaluate(
        IReadOnlyList<IdentityProfile> profiles,
        IReadOnlyList<HostAccount> accounts,
        GitIdentity identity,
        string pushUrl)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(pushUrl);

        IdentityProfile? profile = null;

        // The first that matches, as the page marks the first one current.
        foreach (IdentityProfile candidate in profiles)
        {
            if (candidate.Matches(identity))
            {
                profile = candidate;
                break;
            }
        }

        if (profile is null)
        {
            return PushPermission.WithoutProfile;
        }

        // A path on this machine names no host, so no integration can own it. Redacted all the same: an
        // address git accepts but this parser does not could still carry a credential.
        if (!RemoteUrl.TryParse(pushUrl, out RemoteUrl? remote) || remote is null)
        {
            return PushPermission.Refused(profile, ArgumentRedactor.Redact(pushUrl.Trim()));
        }

        foreach (HostAccount account in accounts)
        {
            if (account.BelongsTo(profile.Id) && account.Owns(remote))
            {
                return PushPermission.Through(profile, remote.Host, account);
            }
        }

        return PushPermission.Refused(profile, remote.Host);
    }
}
