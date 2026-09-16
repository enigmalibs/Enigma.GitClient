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
/// Something went wrong protecting or unprotecting a stored secret.
/// </summary>
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
/// directory created <c>0700</c>. Neither stops a process running as that same user — nothing at
/// this layer can — but both stop another user, a backup, or a synchronised home directory from
/// carrying the tokens away in the clear.
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
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    public FileTokenStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, SecretString secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, StoredEntry> entries = Read();
            entries[key] = Protect(key, secret);
            Write(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SecretString?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return Read().TryGetValue(key, out StoredEntry? entry) ? Unprotect(key, entry) : null;
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
            return true;
        }
        finally
        {
            _gate.Release();
        }
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

    private StoredEntry Protect(string key, SecretString secret)
    {
        byte[] dataKey = ReadOrCreateDataKey();
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

    private SecretString Unprotect(string key, StoredEntry entry)
    {
        byte[] dataKey = ReadOrCreateDataKey();

        try
        {
            byte[] nonce = Convert.FromBase64String(entry.Nonce);
            byte[] cipher = Convert.FromBase64String(entry.Cipher);
            byte[] tag = Convert.FromBase64String(entry.Tag);
            byte[] plaintext = new byte[cipher.Length];

            using AesGcm aes = new(dataKey, TagSize);
            aes.Decrypt(nonce, cipher, tag, plaintext, Encoding.UTF8.GetBytes(key));

            try
            {
                return new SecretString(Encoding.UTF8.GetString(plaintext));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            // Tampered, truncated, or sealed under a different key. Saying so is the only safe
            // answer: returning nothing would look like "no token" and hide a real problem.
            throw new TokenProtectionException(
                $"The stored secret for '{key}' could not be read. It may have been edited, or copied from another machine.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private byte[] ReadOrCreateDataKey()
    {
        string file = _paths.GetConfigurationFile(KeyFileName);

        if (File.Exists(file))
        {
            byte[] stored = File.ReadAllBytes(file);

            try
            {
                return Unwrap(stored);
            }
            catch (CryptographicException exception)
            {
                throw new TokenProtectionException(
                    "The stored tokens belong to a different machine or user account and cannot be read here. "
                    + "Remove them and sign in again.",
                    exception);
            }
        }

        byte[] key = RandomNumberGenerator.GetBytes(KeySize);

        WriteKeyFile(file, Wrap(key));

        return key;
    }

    private static byte[] Wrap(byte[] key)
        => OperatingSystem.IsWindows()
            ? System.Security.Cryptography.ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)
            : key;

    private static byte[] Unwrap(byte[] stored)
        => OperatingSystem.IsWindows()
            ? System.Security.Cryptography.ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)
            : stored;

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
