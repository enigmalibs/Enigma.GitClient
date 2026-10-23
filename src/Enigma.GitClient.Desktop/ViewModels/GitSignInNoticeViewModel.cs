using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Desktop.Navigation;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels;

/// <summary>
/// The repository window's notice that git signed in on its own because an integration's stored token
/// could not be read — what git's own prompt never says (BUG-3024).
/// </summary>
/// <remarks>
/// <para>
/// Said once per session, not at every automatic fetch. A token one integration alone cannot read is
/// said once for that integration; a store this account cannot read at all is said once for all of
/// them, since one reset fixes them all. Dismissing the notice keeps it away for the rest of the
/// session.
/// </para>
/// <para>
/// It goes away by itself once the problem is resolved: the stored tokens reset, or a token stored
/// again — that integration's, or any at all for a store that could not be read, since a store that
/// takes a token can be read again.
/// </para>
/// <para>
/// The resolver and the token store say what happened on whatever thread they ran on; this follows on
/// the UI thread.
/// </para>
/// </remarks>
public sealed class GitSignInNoticeViewModel : ViewModelBase
{
    private readonly ITokenRecovery _recovery;
    private readonly IShellNavigation _shell;

    // The integrations already said this session, by token key, for an unreadable entry.
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);

    // Whether a store that cannot be read at all was already said this session.
    private bool _storeSaid;

    // What the notice on screen is about: the integration's token key, null for the whole store.
    private string? _shownKey;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="resolver">Says when an integration's token could not be read.</param>
    /// <param name="tokens">Says when a token is stored or deleted, or the store reset.</param>
    /// <param name="recovery">Offers the reset.</param>
    /// <param name="shell">Opens the Profiles page, where an integration's token is replaced.</param>
    public GitSignInNoticeViewModel(
        IGitCredentialResolver resolver,
        ITokenStore tokens,
        ITokenRecovery recovery,
        IShellNavigation shell)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(shell);

        _recovery = recovery;
        _shell = shell;

        DismissCommand = new RelayCommand(Hide);
        OpenProfilesCommand = new RelayCommand(() => _shell.GoTo(ShellPage.Profiles));
        ResetCommand = new AsyncRelayCommand(OnResetAsync);

        resolver.TokenUnreadable += (_, e) => Dispatcher.UIThread.Post(() => Show(e.Account, e.Reason));
        tokens.Changed += (_, e) => Dispatcher.UIThread.Post(() => OnTokensChanged(e));
    }

    /// <summary>Gets a value indicating whether the notice is on screen.</summary>
    public bool IsVisible { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets what the notice says.</summary>
    public string Message { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the notice offers the reset: only when the whole store cannot be
    /// read, which nothing else fixes.
    /// </summary>
    public bool OffersReset { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets the command that puts the notice away for the rest of the session.</summary>
    public RelayCommand DismissCommand { get; }

    /// <summary>Gets the command that opens the Profiles page.</summary>
    public RelayCommand OpenProfilesCommand { get; }

    /// <summary>Gets the command that offers to reset the stored tokens.</summary>
    public AsyncRelayCommand ResetCommand { get; }

    /// <summary>
    /// Says that an integration's token could not be read, unless that was said already this session.
    /// </summary>
    /// <param name="account">The integration.</param>
    /// <param name="reason">Why its token could not be read.</param>
    internal void Show(HostAccount account, TokenProtectionReason reason)
    {
        ArgumentNullException.ThrowIfNull(account);

        bool storeWide = reason != TokenProtectionReason.EntryUnreadable;

        if (IsVisible || _storeSaid || (!storeWide && !_said.Add(account.TokenKey)))
        {
            return;
        }

        _storeSaid = storeWide;
        _shownKey = storeWide ? null : account.TokenKey;

        Message = storeWide
            ? $"The stored token for {account.Host} could not be read, so git is using its own sign-in. "
                + "The stored tokens can no longer be read here: reset them, then give each integration its token again."
            : $"The stored token for {account.Host} could not be read, so git is using its own sign-in. "
                + "Replace its token on the Profiles page.";

        OffersReset = storeWide;
        IsVisible = true;
    }

    private void Hide() => IsVisible = false;

    private async Task OnResetAsync() => await _recovery.ConfirmAndResetAsync().ConfigureAwait(true);

    /// <summary>
    /// Puts the notice away, and forgets it was said, once what it was about is resolved.
    /// </summary>
    private void OnTokensChanged(TokenStoreChangedEventArgs e)
    {
        if (e.Change == TokenStoreChange.Reset)
        {
            _said.Clear();
            _storeSaid = false;
            Hide();
            return;
        }

        // A store that took a token can be read again.
        if (e.Change == TokenStoreChange.Written && _storeSaid)
        {
            _storeSaid = false;

            if (IsVisible && _shownKey is null)
            {
                Hide();
            }
        }

        // An integration whose token was written again, or which was disconnected, has nothing left to
        // say — and may be said again should its new token fail too.
        if (e.Key is not null)
        {
            _said.Remove(e.Key);

            if (IsVisible && string.Equals(_shownKey, e.Key, StringComparison.Ordinal))
            {
                Hide();
            }
        }
    }
}
