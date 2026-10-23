using System;
using System.Security.Cryptography;

namespace Enigma.GitClient.Core.Security;

/// <summary>
/// Seals the token store's data key before it is written to disk, and opens it again.
/// </summary>
/// <remarks>
/// The one step of <see cref="FileTokenStore"/> that depends on the operating system, kept apart so it
/// can be replaced — by a test that needs the key to be unreadable on a machine without DPAPI.
/// </remarks>
public interface IDataKeyProtector
{
    /// <summary>
    /// Seals a data key for the disk.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>What is written to the key file.</returns>
    byte[] Protect(byte[] key);

    /// <summary>
    /// Opens a sealed data key.
    /// </summary>
    /// <param name="stored">What the key file holds.</param>
    /// <returns>The key.</returns>
    /// <exception cref="CryptographicException">
    /// The key cannot be opened by this account on this machine — sealed by another user, on another
    /// machine, or with a master key the account no longer has (a Windows password reset).
    /// </exception>
    byte[] Unprotect(byte[] stored);
}

/// <summary>
/// Default <see cref="IDataKeyProtector"/>: DPAPI for the current user on Windows; elsewhere the key is
/// written as it is, and protected by the file's mode (<c>0600</c>) instead.
/// </summary>
public sealed class PlatformDataKeyProtector : IDataKeyProtector
{
    /// <inheritdoc />
    public byte[] Protect(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return OperatingSystem.IsWindows()
            ? ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)
            : key;
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)
            : stored;
    }
}
