using System;
using System.Security.Cryptography;
using System.Text;

namespace Enigma.GitClient.Core.Security;

/// <summary>
/// A secret — an access token, a password — that cannot be printed by accident.
/// </summary>
/// <remarks>
/// <para>
/// The value comes out only through <see cref="Reveal"/>, which is a word that shows up in a code
/// review. <see cref="ToString"/> returns a placeholder, so string interpolation, a log template, an
/// exception message and a debugger watch all show the placeholder rather than the token. That is
/// the whole point: the leaks that happen are the accidental ones.
/// </para>
/// <para>
/// This is not memory protection. The value is an ordinary string on the managed heap; what this
/// type prevents is a secret being written somewhere it can be read later.
/// </para>
/// </remarks>
public sealed class SecretString : IEquatable<SecretString>
{
    /// <summary>
    /// What <see cref="ToString"/> answers instead of the secret.
    /// </summary>
    public const string Placeholder = "***";

    private readonly string _value;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="value">The secret.</param>
    public SecretString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>An empty secret, which is what "no token" looks like.</summary>
    public static SecretString Empty { get; } = new(string.Empty);

    /// <summary>Gets a value indicating whether there is no secret.</summary>
    public bool IsEmpty => _value.Length == 0;

    /// <summary>
    /// Gets how long the secret is. Safe to show: a length is not a credential, and it is what lets
    /// a dialog say "42 characters" rather than nothing.
    /// </summary>
    public int Length => _value.Length;

    /// <summary>
    /// Returns the secret itself.
    /// </summary>
    /// <returns>The value.</returns>
    public string Reveal() => _value;

    /// <summary>
    /// Returns the secret as UTF-8 bytes, for the places that need bytes.
    /// </summary>
    /// <returns>The value's bytes.</returns>
    public byte[] RevealBytes() => Encoding.UTF8.GetBytes(_value);

    /// <inheritdoc />
    /// <remarks>Always the placeholder. That is the reason this type exists.</remarks>
    public override string ToString() => Placeholder;

    /// <inheritdoc />
    /// <remarks>Compared in constant time, so equality cannot be used to guess a secret.</remarks>
    public bool Equals(SecretString? other)
        => other is not null && CryptographicOperations.FixedTimeEquals(RevealBytes(), other.RevealBytes());

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SecretString);

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately only the length. A hash code of the value would be a fingerprint of the secret
    /// that could be logged, compared across processes, or brute-forced offline.
    /// </remarks>
    public override int GetHashCode() => _value.Length;
}
