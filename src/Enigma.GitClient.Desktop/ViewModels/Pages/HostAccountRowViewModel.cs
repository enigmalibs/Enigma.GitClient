using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// Whether an integration's token is there, and can be read.
/// </summary>
public enum HostAccountTokenState
{
    /// <summary>Stored, and it can be read.</summary>
    Stored,

    /// <summary>None stored — after the stored tokens were reset, for instance.</summary>
    Missing,

    /// <summary>Stored, but it cannot be read: damaged, or the whole store is unreadable.</summary>
    Unreadable,
}

/// <summary>
/// One connected account — an integration — as the profiles page lists it: under the profile it
/// belongs to, or among the earlier integrations that belong to none.
/// </summary>
public sealed class HostAccountRowViewModel : ViewModelBase
{
    private readonly ProfilesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="account">The account it stands for.</param>
    /// <param name="provider">The provider that speaks to it.</param>
    /// <param name="moveTargets">
    /// The profiles an earlier integration can be given to; empty for an integration that already
    /// belongs to one.
    /// </param>
    /// <param name="tokenState">Whether its token is there, and can be read.</param>
    public HostAccountRowViewModel(
        ProfilesPageViewModel owner,
        HostAccount account,
        IRepositoryHostProvider? provider,
        IReadOnlyList<IdentityProfile>? moveTargets = null,
        HostAccountTokenState tokenState = HostAccountTokenState.Stored)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(account);

        _owner = owner;
        Account = account;
        Provider = provider;
        TokenState = tokenState;

        List<AccountMoveTargetViewModel> targets = [];

        foreach (IdentityProfile profile in moveTargets ?? [])
        {
            targets.Add(new AccountMoveTargetViewModel(this, profile, owner.MoveAccountCommand));
        }

        MoveTargets = targets;
    }

    /// <summary>Gets the account this row stands for.</summary>
    public HostAccount Account { get; }

    /// <summary>Gets the provider that speaks to it, or <see langword="null"/> in a build without one.</summary>
    public IRepositoryHostProvider? Provider { get; }

    /// <summary>Gets what to call the account.</summary>
    public string DisplayName => Account.DisplayName;

    /// <summary>Gets the login the token belongs to.</summary>
    public string UserName => Account.UserName;

    /// <summary>Gets a value indicating whether the host told us a login.</summary>
    public bool HasUserName => UserName.Length > 0;

    /// <summary>Gets the instance's host name.</summary>
    public string Host => Account.Host;

    /// <summary>Gets what the host is called.</summary>
    public string HostName => Provider?.DisplayName ?? Account.Kind.ToString();

    /// <summary>Gets the line under the account's name: where it is, and who it signs in as.</summary>
    public string Details => HasUserName ? $"{Host} · {UserName}" : Host;

    /// <summary>Gets the icon standing for the kind of host.</summary>
    public PhosphorIcon Icon
        => Account.Kind switch
        {
            HostKind.GitHub => PhosphorIcon.GithubLogo,
            HostKind.GitLab => PhosphorIcon.GitlabLogo,
            HostKind.AzureDevOps => PhosphorIcon.Cloud,
            _ => PhosphorIcon.GlobeSimple,
        };

    /// <summary>Gets the profiles this integration can be moved to.</summary>
    public IReadOnlyList<AccountMoveTargetViewModel> MoveTargets { get; }

    /// <summary>Gets a value indicating whether there is a profile to move it to.</summary>
    public bool HasMoveTargets => MoveTargets.Count > 0;

    /// <summary>Gets whether its token is there, and can be read.</summary>
    public HostAccountTokenState TokenState { get; }

    /// <summary>Gets a value indicating whether the token is missing or cannot be read.</summary>
    public bool HasTokenProblem => TokenState != HostAccountTokenState.Stored;

    /// <summary>Gets what is wrong with the token, for the line under the account.</summary>
    public string TokenProblem
        => TokenState switch
        {
            HostAccountTokenState.Missing => "No token stored: replace its token to sign in with it again.",
            HostAccountTokenState.Unreadable => "The stored token cannot be read: replace it, or disconnect the account.",
            _ => string.Empty,
        };

    /// <summary>Gets the command that gives the account a new token.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> ReplaceTokenCommand => _owner.ReplaceTokenCommand;

    /// <summary>Gets the command that disconnects the account.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> RemoveCommand => _owner.DisconnectAccountCommand;

    /// <summary>Gets the command that lists the account's repositories in a dialog.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> BrowseCommand => _owner.BrowseRepositoriesCommand;

    /// <inheritdoc />
    public override string ToString() => $"{HostName} {DisplayName}";
}

/// <summary>
/// One entry of an earlier integration's <em>Move to</em> menu: the profile it would be given to.
/// </summary>
public sealed class AccountMoveTargetViewModel
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="account">The integration to move.</param>
    /// <param name="profile">The profile to give it to.</param>
    /// <param name="command">The command that moves it, run with this entry.</param>
    public AccountMoveTargetViewModel(
        HostAccountRowViewModel account,
        IdentityProfile profile,
        AsyncRelayCommand<AccountMoveTargetViewModel> command)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(command);

        Account = account;
        Profile = profile;
        Command = command;
    }

    /// <summary>Gets the integration to move.</summary>
    public HostAccountRowViewModel Account { get; }

    /// <summary>Gets the profile to give it to.</summary>
    public IdentityProfile Profile { get; }

    /// <summary>Gets what the menu item says.</summary>
    public string Header => Profile.Label;

    /// <summary>Gets the command that moves it.</summary>
    public AsyncRelayCommand<AccountMoveTargetViewModel> Command { get; }
}
