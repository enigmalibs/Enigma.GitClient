using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// The fields of the "connect an account" dialog, and the validation that decides whether its
/// primary button is enabled.
/// </summary>
/// <remarks>
/// The token is held as an ordinary string only while it is being typed, because that is what a
/// <c>TextBox</c> binds to. It becomes a <see cref="SecretString"/> the moment it leaves this
/// ViewModel, and nothing here ever logs it or puts it in a message.
/// </remarks>
public sealed class AddHostAccountDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="providers">The hosts that can be connected to.</param>
    public AddHostAccountDialogViewModel(IReadOnlyList<IRepositoryHostProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        Providers = providers;
        SelectedProvider = providers.Count > 0 ? providers[0] : null;
    }

    /// <summary>Gets the hosts that can be connected to.</summary>
    public IReadOnlyList<IRepositoryHostProvider> Providers { get; }

    /// <summary>Gets a value indicating whether there is more than one host to choose between.</summary>
    public bool HasChoice => Providers.Count > 1;

    /// <summary>
    /// Gets or sets the host to connect to.
    /// </summary>
    public IRepositoryHostProvider? SelectedProvider
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                // The instance is the one thing that differs between a public account and a
                // self-hosted one, so it starts on the public instance and stays editable.
                InstanceUrl = value?.DefaultBaseUri.ToString().TrimEnd('/') ?? string.Empty;

                OnPropertyChanged(nameof(ScopeHint));
                OnPropertyChanged(nameof(TokenLabel));
                RaiseValidation();
            }
        }
    }

    /// <summary>
    /// Gets or sets the instance's root, which is what a self-hosted instance changes.
    /// </summary>
    public string InstanceUrl
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the personal access token, while it is being typed.
    /// </summary>
    public string Token
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets what to call this account in the interface, empty for the instance's host name.
    /// </summary>
    public string DisplayName { get; set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets the sentence saying which token scopes the host needs.</summary>
    public string ScopeHint => SelectedProvider?.TokenScopeHint ?? string.Empty;

    /// <summary>Gets the label above the token box, which names the host.</summary>
    public string TokenLabel
        => SelectedProvider is { } provider ? $"{provider.DisplayName} personal access token" : "Personal access token";

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            if (SelectedProvider is null)
            {
                return "This build has no hosting providers.";
            }

            if (InstanceUrl.Trim().Length == 0)
            {
                return string.Empty;
            }

            if (!TryBuildUri(out _))
            {
                return "That is not a valid https address. It should look like https://github.com.";
            }

            // A token that has not been typed yet is not an error to shout about; it simply
            // leaves the primary button disabled.
            return string.Empty;
        }
    }

    /// <summary>Gets a value indicating whether there is something to show in the validation line.</summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>Gets a value indicating whether the dialog can be confirmed.</summary>
    public bool IsValid
        => SelectedProvider is not null
            && TryBuildUri(out _)
            && Token.Trim().Length > 0;

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed, so the dialog can
    /// enable or disable its primary button.
    /// </summary>
    public event EventHandler? ValidationChanged;

    /// <summary>
    /// Builds the account the fields describe.
    /// </summary>
    /// <returns>The account, with no identity yet: the host is asked who the token belongs to.</returns>
    public HostAccount ToAccount()
    {
        if (SelectedProvider is null || !TryBuildUri(out Uri? uri) || uri is null)
        {
            throw new InvalidOperationException("The dialog is not valid.");
        }

        return HostAccount.Create(SelectedProvider.Kind, uri, string.Empty, DisplayName.Trim());
    }

    /// <summary>
    /// Takes the token out of the dialog.
    /// </summary>
    /// <returns>The token.</returns>
    public SecretString ToToken() => new(Token.Trim());

    private bool TryBuildUri(out Uri? uri)
    {
        string value = InstanceUrl.Trim().TrimEnd('/');

        if (value.Length == 0)
        {
            uri = null;
            return false;
        }

        // A host name with no scheme is what people type; https is the only scheme worth assuming.
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Host.Length > 0;
    }

    private void RaiseValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(IsValid));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
}
