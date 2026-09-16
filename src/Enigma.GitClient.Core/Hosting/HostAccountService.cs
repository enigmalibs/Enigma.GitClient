using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// The accounts the user has connected, and the tokens that go with them.
/// </summary>
/// <remarks>
/// The account list and the tokens are kept apart on purpose: the list is ordinary JSON that can be
/// read, backed up and looked at, and the tokens are encrypted beside it under keys the list names.
/// Removing an account removes its token, so a forgotten account never leaves a live credential
/// behind.
/// </remarks>
public interface IHostAccountService
{
    /// <summary>
    /// Reads every connected account.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts, in the order they were added.</returns>
    Task<IReadOnlyList<HostAccount>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects an account and stores its token.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="token">The token.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The account as it was stored.</returns>
    Task<HostAccount> AddAsync(HostAccount account, SecretString token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a connected account, and its token when a new one is given.
    /// </summary>
    /// <param name="account">The account, identified by its id.</param>
    /// <param name="token">A new token, or <see langword="null"/> to keep the stored one.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The account as it was stored.</returns>
    Task<HostAccount> UpdateAsync(
        HostAccount account,
        SecretString? token = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects an account and deletes its token.
    /// </summary>
    /// <param name="accountId">The account's id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when there was an account to remove.</returns>
    Task<bool> RemoveAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an account's token.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The token, or <see langword="null"/> when none is stored.</returns>
    Task<SecretString?> GetTokenAsync(HostAccount account, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IHostAccountService"/>: a JSON list in the configuration directory, with the
/// tokens in the token store beside it.
/// </summary>
public sealed class HostAccountService : IHostAccountService
{
    /// <summary>The file holding the account list.</summary>
    public const string FileName = "host-accounts.json";

    /// <summary>The schema version written into the file.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _paths;
    private readonly ITokenStore _tokens;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="tokens">Where the tokens are kept.</param>
    public HostAccountService(IAppPaths paths, ITokenStore tokens)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tokens);

        _paths = paths;
        _tokens = tokens;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostAccount>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return Read();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<HostAccount> AddAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(token);

        // The token first: an account in the list with no token behind it is an account that fails
        // on its next call for no visible reason.
        await _tokens.SetAsync(account.TokenKey, token, cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<HostAccount> accounts = [.. Read()];
            accounts.RemoveAll(stored => string.Equals(stored.Id, account.Id, StringComparison.Ordinal));
            accounts.Add(account);

            Write(accounts);
        }
        finally
        {
            _gate.Release();
        }

        return account;
    }

    /// <inheritdoc />
    public async Task<HostAccount> UpdateAsync(
        HostAccount account,
        SecretString? token = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (token is not null)
        {
            await _tokens.SetAsync(account.TokenKey, token, cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<HostAccount> accounts = [.. Read()];
            int index = accounts.FindIndex(stored => string.Equals(stored.Id, account.Id, StringComparison.Ordinal));

            if (index < 0)
            {
                accounts.Add(account);
            }
            else
            {
                accounts[index] = account;
            }

            Write(accounts);
        }
        finally
        {
            _gate.Release();
        }

        return account;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        HostAccount? removed = null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<HostAccount> accounts = [.. Read()];
            int index = accounts.FindIndex(stored => string.Equals(stored.Id, accountId, StringComparison.Ordinal));

            if (index < 0)
            {
                return false;
            }

            removed = accounts[index];
            accounts.RemoveAt(index);

            Write(accounts);
        }
        finally
        {
            _gate.Release();
        }

        // Outside the lock, and after the list is written: a token left behind is a live credential
        // nothing points at any more.
        await _tokens.DeleteAsync(removed.TokenKey, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public Task<SecretString?> GetTokenAsync(HostAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        return _tokens.TryGetAsync(account.TokenKey, cancellationToken);
    }

    private IReadOnlyList<HostAccount> Read()
    {
        string file = _paths.GetConfigurationFile(FileName);

        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            StoredDocument? document = JsonSerializer.Deserialize<StoredDocument>(
                File.ReadAllText(file),
                SerializerOptions);

            return document?.Accounts is null ? [] : document.Accounts;
        }
        catch (JsonException)
        {
            // A list that cannot be read is an empty list rather than a broken application: the
            // tokens are still there, and adding the account again rewrites the file.
            return [];
        }
    }

    private void Write(List<HostAccount> accounts)
    {
        string file = _paths.GetConfigurationFile(FileName);
        string json = JsonSerializer.Serialize(new StoredDocument(CurrentVersion, accounts), SerializerOptions);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(file, json);
            return;
        }

        // No secrets in here, but it still names the instances a user connects to, which is theirs.
        using FileStream stream = new(
            file,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });

        stream.Write(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// The file's shape.
    /// </summary>
    /// <param name="Version">The schema version.</param>
    /// <param name="Accounts">The accounts.</param>
    private sealed record StoredDocument(int Version, List<HostAccount> Accounts);
}
