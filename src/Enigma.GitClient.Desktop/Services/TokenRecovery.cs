using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Security;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The way out of stored tokens this account can no longer read: what to tell the user, and the reset
/// they may choose.
/// </summary>
/// <remarks>
/// A Windows password reset takes the master key the token store's key was sealed with, and nothing
/// brings those tokens back. The application survives it: every place such a failure reaches the user
/// says so in the same words, and offers the same reset, which is never run without their yes.
/// </remarks>
public interface ITokenRecovery
{
    /// <summary>
    /// Raised once the stored tokens have been reset.
    /// </summary>
    event EventHandler? TokensReset;

    /// <summary>
    /// Says what went wrong, and what fixes it.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The sentence.</returns>
    string Describe(TokenProtectionException exception);

    /// <summary>
    /// Says what went wrong, and what fixes it, from the reason alone — what a check of the store finds.
    /// </summary>
    /// <param name="reason">Why the tokens cannot be read.</param>
    /// <returns>The sentence.</returns>
    string Describe(TokenProtectionReason reason);

    /// <summary>
    /// Asks whether to delete every stored token and, on yes, deletes them.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> once the store has been reset.</returns>
    Task<bool> ConfirmAndResetAsync();

    /// <summary>
    /// Finds out whether the stored tokens can be read, and which cannot.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status.</returns>
    Task<TokenStoreStatus> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ITokenRecovery"/>.
/// </summary>
public sealed class TokenRecovery : ITokenRecovery
{
    /// <summary>What the reset is called, on every button that offers it.</summary>
    public const string ResetLabel = "Reset stored tokens";

    /// <summary>The question the reset asks first.</summary>
    public const string ResetQuestion =
        "Every stored token is deleted, for every integration of every profile. Each connected account "
        + "then needs its token again: connect it again, or replace its token on the Profiles page.\n\n"
        + "Nothing on the host is touched, the integrations stay listed, and repositories you have cloned "
        + "keep working. This cannot be undone.";

    private readonly ITokenStore _tokens;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<TokenRecovery> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="tokens">The store that is reset.</param>
    /// <param name="dialogs">Asks before the reset.</param>
    /// <param name="infoBar">Says the reset happened, or why it could not.</param>
    /// <param name="logger">Receives the reset, and a reset that failed.</param>
    public TokenRecovery(
        ITokenStore tokens,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<TokenRecovery> logger)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _tokens = tokens;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? TokensReset;

    /// <summary>
    /// Finds the token failure inside an exception — itself, or one it wraps.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The failure, or <see langword="null"/> when there is none.</returns>
    public static TokenProtectionException? FindIn(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            TokenProtectionException failure => failure,
            AggregateException aggregate => FindInAll(aggregate),
            { InnerException: { } inner } => FindIn(inner),
            _ => null,
        };
    }

    /// <inheritdoc />
    public string Describe(TokenProtectionException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return Describe(exception.Reason);
    }

    /// <inheritdoc />
    public string Describe(TokenProtectionReason reason)
        => reason switch
        {
            TokenProtectionReason.EntryUnreadable
                => "The stored token of this integration cannot be read. Replace its token, or disconnect it.",
            TokenProtectionReason.FileUnreadable
                => "The file the tokens are stored in cannot be read. Reset the stored tokens, then give each "
                    + "integration its token again.",
            _ => "The stored tokens can no longer be read on this computer or by this account — a Windows "
                + "password reset does this. Reset the stored tokens, then give each integration its token again.",
        };

    /// <inheritdoc />
    public async Task<bool> ConfirmAndResetAsync()
    {
        if (!await _dialogs.ConfirmDestructiveAsync(ResetLabel, ResetQuestion, ResetLabel).ConfigureAwait(true))
        {
            return false;
        }

        try
        {
            await _tokens.ResetAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Resetting the stored tokens failed");

            _infoBar.Notify("Could not reset the stored tokens", exception.Message, InfoBarSeverity.Error);

            return false;
        }

        _logger.LogInformation("The stored tokens were reset");

        _infoBar.Notify(
            "Stored tokens reset",
            "Give each integration its token again.",
            InfoBarSeverity.Success);

        TokensReset?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <inheritdoc />
    public Task<TokenStoreStatus> CheckAsync(CancellationToken cancellationToken = default)
        => _tokens.CheckAsync(cancellationToken);

    private static TokenProtectionException? FindInAll(AggregateException aggregate)
    {
        foreach (Exception inner in aggregate.InnerExceptions)
        {
            if (FindIn(inner) is { } failure)
            {
                return failure;
            }
        }

        return null;
    }
}
