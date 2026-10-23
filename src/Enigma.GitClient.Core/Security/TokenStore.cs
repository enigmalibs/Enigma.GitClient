using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;

namespace Enigma.GitClient.Core.Security;

/// <summary>
/// Why a stored secret could not be read.
/// </summary>
public enum TokenProtectionReason
{
    /// <summary>Not said: the exception was made by code other than the store.</summary>
    Unspecified,

    /// <summary>
    /// The data key cannot be opened — sealed for another user or machine, or with a master key this
    /// account no longer has (a Windows password reset) — or is missing or damaged while secrets
    /// sealed with it remain. The whole store is unreadable for this account.
    /// </summary>
    KeyUnavailable,

    /// <summary>One entry is damaged, was edited, or was sealed under another key.</summary>
    EntryUnreadable,

    /// <summary>The tokens file itself cannot be read.</summary>
    FileUnreadable,
}

/// <summary>
/// Something went wrong protecting or unprotecting a stored secret.
/// </summary>
/// <remarks>
/// <see cref="Reason"/> tells the three failures apart: the whole store, which only a reset fixes,
/// and one entry, which replacing or removing that one secret fixes.
/// </remarks>
public sealed class TokenProtectionException : Exception
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public TokenProtectionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    public TokenProtectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with no message.
    /// </summary>
    public TokenProtectionException()
        : base("A stored secret could not be read.")
    {
    }

    /// <summary>
    /// Initialises a new instance that says why.
    /// </summary>
    /// <param name="reason">Why the secret could not be read.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    /// <param name="key">The key of the entry, for <see cref="TokenProtectionReason.EntryUnreadable"/>.</param>
    public TokenProtectionException(
        TokenProtectionReason reason,
        string message,
        Exception? innerException = null,
        string? key = null)
        : base(message, innerException)
    {
        Reason = reason;
        Key = key;
    }

    /// <summary>Gets why the secret could not be read.</summary>
    public TokenProtectionReason Reason { get; }

    /// <summary>
    /// Gets the key of the entry that could not be read, or <see langword="null"/> when the failure is
    /// not one entry's.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Gets a value indicating whether the failure is the whole store's rather than one entry's — what
    /// only a reset of the store fixes.
    /// </summary>
    public bool IsStoreWide => Reason != TokenProtectionReason.EntryUnreadable;
}

/// <summary>
/// What the token store holds, and whether it can be read.
/// </summary>
/// <param name="Failure">
/// Why the whole store cannot be read, or <see langword="null"/> when it can.
/// </param>
/// <param name="Keys">The keys that hold a secret, as far as the file can be read.</param>
/// <param name="UnreadableKeys">The keys whose secret cannot be read, while the store itself can be.</param>
public sealed record TokenStoreStatus(
    TokenProtectionReason? Failure,
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> UnreadableKeys)
{
    /// <summary>Gets the status of a store that holds nothing.</summary>
    public static TokenStoreStatus Empty { get; } = new(null, [], []);

    /// <summary>Gets a value indicating whether the store as a whole can be read.</summary>
    public bool IsReadable => Failure is null;

    /// <summary>
    /// Answers whether a key holds a secret that can be read.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when there is one and it opens.</returns>
    public bool CanRead(string key)
        => IsReadable && Contains(Keys, key) && !Contains(UnreadableKeys, key);

    /// <summary>
    /// Answers whether a key holds a secret, readable or not.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the file has an entry under it.</returns>
    public bool Holds(string key) => Contains(Keys, key);

    private static bool Contains(IReadOnlyList<string> keys, string key)
    {
        foreach (string candidate in keys)
        {
            if (string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// What changed in the token store.
/// </summary>
public enum TokenStoreChange
{
    /// <summary>A secret was stored.</summary>
    Written,

    /// <summary>A secret was deleted.</summary>
    Deleted,

    /// <summary>Every secret was deleted, with what sealed them.</summary>
    Reset,
}

/// <summary>
/// Says what changed in the token store.
/// </summary>
public sealed class TokenStoreChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="change">What changed.</param>
    /// <param name="key">The key written or deleted, or <see langword="null"/> for a reset.</param>
    public TokenStoreChangedEventArgs(TokenStoreChange change, string? key)
    {
        Change = change;
        Key = key;
    }

    /// <summary>Gets what changed.</summary>
    public TokenStoreChange Change { get; }

    /// <summary>Gets the key written or deleted, or <see langword="null"/> for a reset.</summary>
    public string? Key { get; }
}

/// <summary>
/// Where access tokens are kept.
/// </summary>
/// <remarks>
/// Keys are opaque strings the caller chooses; <see cref="Hosting.HostAccount.TokenKey"/> is what
/// the hosting integrations use.
/// </remarks>
public interface ITokenStore
{
    /// <summary>
    /// Raised once a secret has been stored or deleted, or the store reset — what a warning that a token
    /// could not be read waits for to go away.
    /// </summary>
    /// <remarks>Raised on whatever thread the change ran on, after the store is free again.</remarks>
    event EventHandler<TokenStoreChangedEventArgs>? Changed;

    /// <summary>
    /// Stores a secret, replacing whatever was under that key.
    /// </summary>
    /// <param name="key">The key to store it under.</param>
    /// <param name="secret">The secret.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once it is stored.</returns>
    Task SetAsync(string key, SecretString secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a secret.
    /// </summary>
    /// <param name="key">The key it was stored under.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The secret, or <see langword="null"/> when there is none under that key.</returns>
    Task<SecretString?> TryGetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a secret.
    /// </summary>
    /// <param name="key">The key it was stored under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when there was one to remove.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the keys that hold a secret.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The keys, in no particular order.</returns>
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every stored secret, and what they were sealed with: the way out of a store this
    /// account can no longer read. The next secret stored starts a new store.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for the store.</param>
    /// <returns>A task that completes once the store is gone.</returns>
    /// <remarks>Never called on the store's own initiative: only a person decides to lose every token.</remarks>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds out whether the store can be read, and which secrets cannot — without handing any of them
    /// out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status; a store that cannot be read is reported, not thrown.</returns>
    Task<TokenStoreStatus> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps secrets in a file in the user's configuration directory, encrypted with AES-GCM.
/// </summary>
/// <remarks>
/// <para>
/// Every entry is sealed under one random 256-bit data key with its own 96-bit nonce, and the key
/// the entry is stored under is fed in as associated data — so an entry cannot be moved to another
/// key, and a file edited by hand fails authentication instead of decrypting to something.
/// </para>
/// <para>
/// The data key itself is the part that cannot be encrypted by this file: on Windows it is
/// protected with DPAPI for the current user, and on Linux it is a file created <c>0600</c> inside a
/// directory created <c>0700</c> (<see cref="IDataKeyProtector"/>). Neither stops a process running as
/// that same user — nothing at this layer can — but both stop another user, a backup, or a
/// synchronised home directory from carrying the tokens away in the clear.
/// </para>
/// <para>
/// A key that cannot be opened any more — a Windows password reset takes the DPAPI master key it was
/// sealed with — makes every secret unreadable, and nothing can bring them back. The store says so
/// (<see cref="TokenProtectionReason.KeyUnavailable"/>) rather than minting a new key over them, and
/// <see cref="ResetAsync"/> is the way out, which a person chooses.
/// </para>
/// </remarks>
public sealed class FileTokenStore : ITokenStore
{
    /// <summary>The file holding the encrypted entries.</summary>
    public const string FileName = "tokens.json";

    /// <summary>The file holding the data key.</summary>
    public const string KeyFileName = "tokens.key";

    /// <summary>The schema version written into the file.</summary>
    public const int CurrentVersion = 1;

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _paths;
    private readonly IDataKeyProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="protector">Seals the data key for the disk, and opens it again.</param>
    public FileTokenStore(IAppPaths paths, IDataKeyProtector protector)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(protector);

        _paths = paths;
        _protector = protector;
    }

    /// <inheritdoc />
    public event EventHandler<TokenStoreChangedEventArgs>? Changed;

    /// <inheritdoc />
    public async Task SetAsync(string key, SecretString secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, StoredEntry> entries = Read();
            entries[key] = Protect(key, secret, DataKeyFor(entries, key));
            Write(entries);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, new TokenStoreChangedEventArgs(TokenStoreChange.Written, key));
    }

    /// <inheritdoc />
    public async Task<SecretString?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!Read().TryGetValue(key, out StoredEntry? entry))
            {
                return null;
            }

            byte[] dataKey = ReadDataKey() ?? throw MissingKey();

            try
            {
                return Unprotect(key, entry, dataKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, StoredEntry> entries = Read();

            if (!entries.Remove(key))
            {
                return false;
            }

            Write(entries);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, new TokenStoreChangedEventArgs(TokenStoreChange.Deleted, key));
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return [.. Read().Keys];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The secrets first, then what sealed them: a reset stopped half-way leaves no secret behind
            // a key, and the next reset finishes it.
            File.Delete(_paths.GetConfigurationFile(FileName));
            File.Delete(_paths.GetConfigurationFile(KeyFileName));
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, new TokenStoreChangedEventArgs(TokenStoreChange.Reset, null));
    }

    /// <inheritdoc />
    public async Task<TokenStoreStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, StoredEntry> entries;

            try
            {
                entries = Read();
            }
            catch (TokenProtectionException exception)
            {
                return new TokenStoreStatus(exception.Reason, [], []);
            }

            List<string> keys = [.. entries.Keys];
            byte[]? dataKey;

            try
            {
                dataKey = ReadDataKey();
            }
            catch (TokenProtectionException exception)
            {
                return new TokenStoreStatus(exception.Reason, keys, []);
            }

            if (dataKey is null)
            {
                return keys.Count == 0 ? TokenStoreStatus.Empty : new TokenStoreStatus(TokenProtectionReason.KeyUnavailable, keys, []);
            }

            List<string> unreadable = [];

            try
            {
                foreach ((string key, StoredEntry entry) in entries)
                {
                    try
                    {
                        CryptographicOperations.ZeroMemory(Open(key, entry, dataKey));
                    }
                    catch (TokenProtectionException)
                    {
                        unreadable.Add(key);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }

            return new TokenStoreStatus(null, keys, unreadable);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static StoredEntry Protect(string key, SecretString secret, byte[] dataKey)
    {
        byte[] plaintext = secret.RevealBytes();
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using AesGcm aes = new(dataKey, TagSize);
            aes.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes(key));

            return new StoredEntry(
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(cipher),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static SecretString Unprotect(string key, StoredEntry? entry, byte[] dataKey)
    {
        byte[] plaintext = Open(key, entry, dataKey);

        try
        {
            return new SecretString(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Authenticates and decrypts one entry.
    /// </summary>
    /// <returns>The plaintext, which the caller zeroes.</returns>
    /// <exception cref="TokenProtectionException">The entry cannot be read under this key.</exception>
    private static byte[] Open(string key, StoredEntry? entry, byte[] dataKey)
    {
        try
        {
            if (entry is null)
            {
                throw new FormatException("The entry is empty.");
            }

            byte[] nonce = Convert.FromBase64String(entry.Nonce);
            byte[] cipher = Convert.FromBase64String(entry.Cipher);
            byte[] tag = Convert.FromBase64String(entry.Tag);
            byte[] plaintext = new byte[cipher.Length];

            using AesGcm aes = new(dataKey, TagSize);
            aes.Decrypt(nonce, cipher, tag, plaintext, Encoding.UTF8.GetBytes(key));

            return plaintext;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            // Tampered, truncated, or sealed under a different key. Saying so is the only safe
            // answer: returning nothing would look like "no token" and hide a real problem.
            throw new TokenProtectionException(
                TokenProtectionReason.EntryUnreadable,
                $"The stored secret for '{key}' could not be read. It may have been edited, or copied from another machine.",
                exception,
                key);
        }
    }

    /// <summary>
    /// The data key to seal a new secret with: the store's own, or — when there is none and no other
    /// secret needs it — a new one.
    /// </summary>
    private byte[] DataKeyFor(Dictionary<string, StoredEntry> entries, string key)
    {
        if (ReadDataKey() is { } existing)
        {
            return existing;
        }

        foreach (string other in entries.Keys)
        {
            if (!string.Equals(other, key, StringComparison.Ordinal))
            {
                // A new key would leave every other secret sealed under one that is gone, for good and
                // without a word: that is the reset's to decide, not a write's.
                throw MissingKey();
            }
        }

        byte[] created = RandomNumberGenerator.GetBytes(KeySize);

        WriteKeyFile(_paths.GetConfigurationFile(KeyFileName), _protector.Protect(created));

        return created;
    }

    /// <summary>
    /// Reads and opens the data key.
    /// </summary>
    /// <returns>The key, or <see langword="null"/> when there is no key file.</returns>
    /// <exception cref="TokenProtectionException">The key cannot be opened, or is not a key.</exception>
    private byte[]? ReadDataKey()
    {
        string file = _paths.GetConfigurationFile(KeyFileName);

        if (!File.Exists(file))
        {
            return null;
        }

        byte[] stored = File.ReadAllBytes(file);
        byte[] key;

        try
        {
            key = _protector.Unprotect(stored);
        }
        catch (CryptographicException exception)
        {
            throw new TokenProtectionException(
                TokenProtectionReason.KeyUnavailable,
                "The stored tokens belong to a different machine or user account and cannot be read here. "
                + "Remove them and sign in again.",
                exception);
        }

        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);

            throw new TokenProtectionException(
                TokenProtectionReason.KeyUnavailable,
                "The key the stored tokens were sealed with is damaged, so they cannot be read. "
                + "Remove them and sign in again.");
        }

        return key;
    }

    private static TokenProtectionException MissingKey()
        => new(
            TokenProtectionReason.KeyUnavailable,
            "The key the stored tokens were sealed with is missing, so they cannot be read. "
            + "Remove them and sign in again.");

    private static void WriteKeyFile(string path, byte[] contents)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, contents);
            return;
        }

        // Created with the right mode rather than tightened afterwards: between the two there is a
        // window in which the key is world-readable.
        using FileStream stream = new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });

        stream.Write(contents);
    }

    private Dictionary<string, StoredEntry> Read()
    {
        string file = _paths.GetConfigurationFile(FileName);

        if (!File.Exists(file))
        {
            return [];
        }

        StoredDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<StoredDocument>(File.ReadAllText(file), SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new TokenProtectionException(
                TokenProtectionReason.FileUnreadable,
                "The stored tokens file is not readable. Remove it and sign in again.",
                exception);
        }

        return document?.Entries is null ? [] : new Dictionary<string, StoredEntry>(document.Entries, StringComparer.Ordinal);
    }

    private void Write(Dictionary<string, StoredEntry> entries)
    {
        string file = _paths.GetConfigurationFile(FileName);
        string json = JsonSerializer.Serialize(new StoredDocument(CurrentVersion, entries), SerializerOptions);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(file, json);
            return;
        }

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
    /// One encrypted secret.
    /// </summary>
    /// <param name="Nonce">The nonce, base-64.</param>
    /// <param name="Cipher">The ciphertext, base-64.</param>
    /// <param name="Tag">The authentication tag, base-64.</param>
    private sealed record StoredEntry(string Nonce, string Cipher, string Tag);

    /// <summary>
    /// The file's shape.
    /// </summary>
    /// <param name="Version">The schema version.</param>
    /// <param name="Entries">The entries, by key.</param>
    private sealed record StoredDocument(
        int Version,
        [property: JsonPropertyName("entries")] Dictionary<string, StoredEntry> Entries);
}
